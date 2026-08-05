using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using VvvvMcp.Core.Models;

namespace VvvvMcp.Core.Services;

/// <summary>
/// SQLite FTS5-backed search index replacing in-memory term-frequency search.
///
/// Three FTS5 virtual tables:
///   knowledge_fts  – one row per knowledge .md file
///   nodes_fts      – one row per node in the catalog (6,400+ nodes)
///   practical_fts  – help patches, forum solutions, code snippets, changelogs
///
/// BM25 ranking is used for all three, with per-column weights so node name
/// matches rank higher than summary matches, etc.
///
/// The index is rebuilt every startup from in-memory data (sub-second for 6K+
/// nodes + 23 knowledge files). Practical data is rebuilt from JSON files on disk.
/// </summary>
public sealed class SearchIndexService : IDisposable
{
    private readonly ILogger<SearchIndexService> _logger;
    private SqliteConnection? _db;

    public bool IsReady { get; private set; }

    // Column weights for bm25(): higher = more important.
    // Note: bm25() returns NEGATIVE scores; lower value = better match.
    // knowledge_fts columns: description(0), content(1), name(2, unindexed)
    private const double KwDesc = 5.0, KwContent = 1.0;
    // nodes_fts columns: name(0), category(1), summary(2), tags(3), full_name(4), package(5, unindexed), type_name(6, unindexed)
    private const double NwName = 10.0, NwCategory = 5.0, NwSummary = 2.0, NwTags = 3.0, NwFullName = 8.0;
    // practical_fts columns: title(0), content(1), code(2)
    private const double PwTitle = 5.0, PwContent = 2.0, PwCode = 3.0;

    public SearchIndexService(ILogger<SearchIndexService> logger)
    {
        _logger = logger;
    }

    // ── Initialization ────────────────────────────────────────────────────────

    public async Task InitializeAsync(string dbPath, CancellationToken ct = default)
    {
        IsReady = false;

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode        = SqliteOpenMode.ReadWriteCreate,
            Cache       = SqliteCacheMode.Shared
        }.ToString();

        _db = new SqliteConnection(connectionString);
        await _db.OpenAsync(ct);

        // Enable WAL for better concurrent reads
        await ExecuteAsync("PRAGMA journal_mode=WAL;");
        await ExecuteAsync("PRAGMA synchronous=NORMAL;");

        await CreateTablesAsync(ct);

        IsReady = true;
        _logger.LogInformation("Search index initialized at {Path}", dbPath);
    }

    // Schema version — bump when FTS table definitions change; tables are dropped
    // and recreated (they are rebuilt from source data on every startup anyway).
    // v2: nodes_fts.full_name is now INDEXED (enables "Rotation (Successive)" lookups).
    private const int SchemaVersion = 2;

    private async Task CreateTablesAsync(CancellationToken ct)
    {
        // FTS5 with unicode61 tokenizer:
        //   - splits on whitespace and most punctuation (dots, hyphens are separators)
        //   - handles Unicode normalization
        //   - prefix queries (term*) work on any indexed column
        //   - case-insensitive by default
        //
        // We use 'unicode61' without custom tokenchars so that:
        //   searching "VL" matches "VL.CoreLib", "VL.Stride", etc.
        //   searching "TransformSRT" matches exactly (alphanum, not split)
        //   searching "3D" matches "3D.Transform", "3D.Scene", etc.

        var currentVersion = 0;
        using (var cmd = _db!.CreateCommand())
        {
            cmd.CommandText = "PRAGMA user_version;";
            currentVersion = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        }

        if (currentVersion < SchemaVersion)
        {
            await ExecuteAsync("DROP TABLE IF EXISTS knowledge_fts;");
            await ExecuteAsync("DROP TABLE IF EXISTS nodes_fts;");
            await ExecuteAsync("DROP TABLE IF EXISTS practical_fts;");
            await ExecuteAsync($"PRAGMA user_version = {SchemaVersion};");
        }

        await ExecuteAsync("""
            CREATE VIRTUAL TABLE IF NOT EXISTS knowledge_fts USING fts5(
                description,
                content,
                name        UNINDEXED,
                tokenize    = 'unicode61'
            );
            """);

        await ExecuteAsync("""
            CREATE VIRTUAL TABLE IF NOT EXISTS nodes_fts USING fts5(
                name,
                category,
                summary,
                tags,
                full_name,
                package     UNINDEXED,
                type_name   UNINDEXED,
                tokenize    = 'unicode61'
            );
            """);

        await ExecuteAsync("""
            CREATE VIRTUAL TABLE IF NOT EXISTS practical_fts USING fts5(
                title,
                content,
                code,
                source      UNINDEXED,
                url         UNINDEXED,
                author      UNINDEXED,
                meta        UNINDEXED,
                tokenize    = 'unicode61'
            );
            """);
    }

    // ── Indexing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the knowledge_fts table from the loaded KnowledgeService files.
    /// Called at startup after KnowledgeService.LoadAsync().
    /// </summary>
    public async Task RebuildKnowledgeIndexAsync(IEnumerable<KnowledgeFile> files, CancellationToken ct = default)
    {
        EnsureReady();

        using var tx = _db!.BeginTransaction();
        await ExecuteAsync("DELETE FROM knowledge_fts;", tx);

        var cmd = _db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO knowledge_fts(description, content, name)
            VALUES ($desc, $content, $name);
            """;
        var pDesc    = cmd.Parameters.Add("$desc",    SqliteType.Text);
        var pContent = cmd.Parameters.Add("$content", SqliteType.Text);
        var pName    = cmd.Parameters.Add("$name",    SqliteType.Text);

        int count = 0;
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            pDesc.Value    = f.Description;
            pContent.Value = f.Content;
            pName.Value    = f.Name;
            await cmd.ExecuteNonQueryAsync(ct);
            count++;
        }

        tx.Commit();
        _logger.LogInformation("Indexed {Count} knowledge files into FTS5", count);
    }

    /// <summary>
    /// Rebuilds the nodes_fts table from the loaded NodeCatalogService nodes.
    /// Called at startup after NodeCatalogService.LoadAsync().
    /// </summary>
    public async Task RebuildNodeIndexAsync(IEnumerable<VvvvNode> nodes, CancellationToken ct = default)
    {
        EnsureReady();

        using var tx = _db!.BeginTransaction();
        await ExecuteAsync("DELETE FROM nodes_fts;", tx);

        var cmd = _db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO nodes_fts(name, category, summary, tags, full_name, package, type_name)
            VALUES ($name, $cat, $sum, $tags, $full, $pkg, $type);
            """;
        var pName  = cmd.Parameters.Add("$name", SqliteType.Text);
        var pCat   = cmd.Parameters.Add("$cat",  SqliteType.Text);
        var pSum   = cmd.Parameters.Add("$sum",  SqliteType.Text);
        var pTags  = cmd.Parameters.Add("$tags", SqliteType.Text);
        var pFull  = cmd.Parameters.Add("$full", SqliteType.Text);
        var pPkg   = cmd.Parameters.Add("$pkg",  SqliteType.Text);
        var pType  = cmd.Parameters.Add("$type", SqliteType.Text);

        int count = 0;
        foreach (var n in nodes)
        {
            ct.ThrowIfCancellationRequested();
            pName.Value  = n.Name;
            pCat.Value   = n.Category;
            pSum.Value   = n.Summary;
            pTags.Value  = string.Join(" ", n.Tags);
            pFull.Value  = n.FullName;
            pPkg.Value   = n.Package;
            pType.Value  = n.Type.ToString();
            await cmd.ExecuteNonQueryAsync(ct);
            count++;
        }

        tx.Commit();
        _logger.LogInformation("Indexed {Count} nodes into FTS5", count);
    }

    /// <summary>
    /// Builds the practical_fts table from JSON files produced by index-help-patches.ps1
    /// and scrape-forum.ps1, plus any knowledge/*.md files not already in knowledge_fts.
    /// Called at startup; safe to call even if source files don't exist yet.
    /// </summary>
    public async Task RebuildPracticalIndexAsync(string knowledgeDir, CancellationToken ct = default)
    {
        EnsureReady();

        using var tx = _db!.BeginTransaction();
        await ExecuteAsync("DELETE FROM practical_fts;", tx);

        int total = 0;

        // ── 1. help_index.json (per-node → file list) ────────────────────────
        // Walk up from knowledgeDir to find VVVVNodeAnalyzer/output/ — works
        // in both dev layout (binary is 4-5 levels above repo root) and
        // bundled tool (output dir is sibling of knowledge/).
        var outputDir      = FindOutputDir(knowledgeDir);
        var helpIndexPath  = outputDir is not null ? Path.Combine(outputDir, "help_index.json")  : null;
        var forumRawPath   = outputDir is not null ? Path.Combine(outputDir, "forum_raw.json")   : null;

        if (helpIndexPath is not null && File.Exists(helpIndexPath))
            total += await IndexHelpPatchJson(helpIndexPath, tx, ct);

        // ── 2. forum_raw.json (structured topic/post data) ───────────────────
        if (forumRawPath is not null && File.Exists(forumRawPath))
            total += await IndexForumRawJson(forumRawPath, tx, ct);
        var mdSourceFiles = new[]
        {
            ("forum-solution", Path.Combine(knowledgeDir, "vl-forum-solutions.md")),
            ("forum-snippet",  Path.Combine(knowledgeDir, "vl-forum-snippets.md")),
            ("help-example",   Path.Combine(knowledgeDir, "vl-help-examples.md")),
        };

        foreach (var (source, mdPath) in mdSourceFiles)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(mdPath)) continue;
            total += await IndexMarkdownSections(mdPath, source, tx, ct);
        }

        tx.Commit();
        _logger.LogInformation("Indexed {Count} practical knowledge entries into FTS5", total);
    }

    // ── Search ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Full-text search over knowledge files (replaces in-memory term-frequency search).
    /// Returns results matching the existing KnowledgeSearchResult type.
    /// </summary>
    public List<KnowledgeSearchResult> SearchKnowledge(string query, int limit = 5)
    {
        EnsureReady();

        var ftsQuery = BuildFtsQuery(query);
        if (string.IsNullOrEmpty(ftsQuery)) return [];

        var results = new List<KnowledgeSearchResult>();

        using var cmd = _db!.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                name,
                description,
                snippet(knowledge_fts, 1, '', '', '...', 40) AS snip,
                bm25(knowledge_fts, {KwDesc}, {KwContent}) AS rank
            FROM knowledge_fts
            WHERE knowledge_fts MATCH $q
            ORDER BY rank
            LIMIT $lim;
            """;
        cmd.Parameters.AddWithValue("$q",   ftsQuery);
        cmd.Parameters.AddWithValue("$lim", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name  = reader.GetString(0);
            var desc  = reader.GetString(1);
            var snip  = reader.GetString(2);
            var score = -reader.GetDouble(3); // negate: bm25 returns negative
            results.Add(new KnowledgeSearchResult(name, desc, snip, score));
        }

        return results;
    }

    /// <summary>
    /// Returns FTS5 node hits: (FullName, Package, Score).
    /// NodeCatalogService.Search() calls this and looks up full VvvvNode objects.
    /// </summary>
    public IReadOnlyList<(string FullName, string Package, double Score)> SearchNodeHits(
        string query, string? category = null, int limit = 25)
    {
        EnsureReady();

        // Phase 1: AND semantics (all terms must match) — precise.
        var results = RunNodeQuery(BuildFtsQuery(query, orMode: false), category, limit);

        // Phase 2: OR fallback (any term, prefix-matched) — recall.
        // Fixes "0 results" for natural multi-word queries like "box model entity".
        if (results.Count < Math.Min(5, limit))
        {
            var orResults = RunNodeQuery(BuildFtsQuery(query, orMode: true), category, limit * 2);
            var seen = new HashSet<string>(
                results.Select(r => r.FullName + "|" + r.Package),
                StringComparer.OrdinalIgnoreCase);
            foreach (var hit in orResults)
            {
                if (seen.Add(hit.FullName + "|" + hit.Package))
                    results.Add(hit);
                if (results.Count >= limit) break;
            }
            results = results.OrderByDescending(r => r.Score).Take(limit).ToList();
        }

        return results;
    }

    private List<(string FullName, string Package, double Score)> RunNodeQuery(
        string ftsQuery, string? category, int limit)
    {
        var results = new List<(string, string, double)>();
        if (string.IsNullOrEmpty(ftsQuery)) return results;

        using var cmd = _db!.CreateCommand();

        var whereClause = string.IsNullOrEmpty(category)
            ? "WHERE nodes_fts MATCH $q"
            : "WHERE nodes_fts MATCH $q AND category LIKE $cat";

        cmd.CommandText = $"""
            SELECT
                full_name,
                package,
                bm25(nodes_fts, {NwName}, {NwCategory}, {NwSummary}, {NwTags}, {NwFullName}, 0.0, 0.0) AS rank
            FROM nodes_fts
            {whereClause}
            ORDER BY rank
            LIMIT $lim;
            """;
        cmd.Parameters.AddWithValue("$q",   ftsQuery);
        cmd.Parameters.AddWithValue("$lim", limit);
        if (!string.IsNullOrEmpty(category))
            cmd.Parameters.AddWithValue("$cat", $"%{category}%");

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var fullName = reader.GetString(0);
            var package  = reader.GetString(1);
            var score    = -reader.GetDouble(2);
            results.Add((fullName, package, score));
        }

        return results;
    }

    /// <summary>
    /// Search the practical knowledge index: help patches, forum solutions, code snippets.
    /// </summary>
    public List<PracticalSearchResult> SearchPractical(
        string query, string? source = null, int limit = 10)
    {
        EnsureReady();

        var ftsQuery = BuildFtsQuery(query);
        if (string.IsNullOrEmpty(ftsQuery)) return [];

        var results = new List<PracticalSearchResult>();

        using var cmd = _db!.CreateCommand();

        var whereClause = string.IsNullOrEmpty(source)
            ? "WHERE practical_fts MATCH $q"
            : "WHERE practical_fts MATCH $q AND source = $src";

        cmd.CommandText = $"""
            SELECT
                source,
                title,
                snippet(practical_fts, 1, '', '', '...', 50) AS snip,
                snippet(practical_fts, 2, '', '', '...', 30) AS code_snip,
                url,
                author,
                bm25(practical_fts, {PwTitle}, {PwContent}, {PwCode}) AS rank
            FROM practical_fts
            {whereClause}
            ORDER BY rank
            LIMIT $lim;
            """;
        cmd.Parameters.AddWithValue("$q",   ftsQuery);
        cmd.Parameters.AddWithValue("$lim", limit);
        if (!string.IsNullOrEmpty(source))
            cmd.Parameters.AddWithValue("$src", source);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var src      = reader.GetString(0);
            var title    = reader.GetString(1);
            var snip     = reader.GetString(2);
            var codeSnip = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var url      = reader.IsDBNull(4) ? null : reader.GetString(4);
            var author   = reader.IsDBNull(5) ? null : reader.GetString(5);
            var score    = -reader.GetDouble(6);
            results.Add(new PracticalSearchResult(src, title, snip, codeSnip, url, author, score));
        }

        return results;
    }

    /// <summary>Returns indexing stats for diagnostics.</summary>
    public (int Knowledge, int Nodes, int Practical) GetStats()
    {
        if (!IsReady) return (0, 0, 0);

        int Count(string table)
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        return (Count("knowledge_fts"), Count("nodes_fts"), Count("practical_fts"));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<int> IndexHelpPatchJson(
        string path, SqliteTransaction tx, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("nodeIndex", out var nodeIndex))
            return 0;

        var cmd = _db!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO practical_fts(title, content, code, source, url, meta)
            VALUES ($title, $content, $code, $source, $url, $meta);
            """;
        var pTitle   = cmd.Parameters.Add("$title",   SqliteType.Text);
        var pContent = cmd.Parameters.Add("$content", SqliteType.Text);
        var pCode    = cmd.Parameters.Add("$code",    SqliteType.Text);
        var pSource  = cmd.Parameters.Add("$source",  SqliteType.Text);
        var pUrl     = cmd.Parameters.Add("$url",     SqliteType.Text);
        var pMeta    = cmd.Parameters.Add("$meta",    SqliteType.Text);

        int count = 0;
        foreach (var prop in nodeIndex.EnumerateObject())
        {
            ct.ThrowIfCancellationRequested();
            var nodeName = prop.Name;
            var files    = prop.Value.EnumerateArray()
                               .Select(e => e.GetString() ?? "")
                               .Where(s => s.Length > 0)
                               .ToList();
            if (files.Count == 0) continue;

            var fileNames = string.Join(", ", files.Select(Path.GetFileName));
            pTitle.Value   = nodeName;
            pContent.Value = $"Node '{nodeName}' is demonstrated in {files.Count} help patch(es): {fileNames}";
            pCode.Value    = "";
            pSource.Value  = "help-patch";
            pUrl.Value     = DBNull.Value;
            pMeta.Value    = JsonSerializer.Serialize(files);
            await cmd.ExecuteNonQueryAsync(ct);
            count++;
        }

        _logger.LogInformation("Indexed {Count} help-patch node entries from {File}", count, Path.GetFileName(path));
        return count;
    }

    private async Task<int> IndexForumRawJson(
        string path, SqliteTransaction tx, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("topics", out var topics))
            return 0;

        var cmd = _db!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO practical_fts(title, content, code, source, url, author, meta)
            VALUES ($title, $content, $code, $source, $url, $author, $meta);
            """;
        var pTitle   = cmd.Parameters.Add("$title",   SqliteType.Text);
        var pContent = cmd.Parameters.Add("$content", SqliteType.Text);
        var pCode    = cmd.Parameters.Add("$code",    SqliteType.Text);
        var pSource  = cmd.Parameters.Add("$source",  SqliteType.Text);
        var pUrl     = cmd.Parameters.Add("$url",     SqliteType.Text);
        var pAuthor  = cmd.Parameters.Add("$author",  SqliteType.Text);
        var pMeta    = cmd.Parameters.Add("$meta",    SqliteType.Text);

        int count = 0;
        foreach (var topic in topics.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            var title   = topic.TryGetProperty("title",  out var t) ? t.GetString() ?? "" : "";
            var url     = topic.TryGetProperty("url",    out var u) ? u.GetString() ?? "" : "";
            var solved  = topic.TryGetProperty("solved", out var s) && s.GetBoolean();

            if (!topic.TryGetProperty("posts", out var posts)) continue;

            foreach (var post in posts.EnumerateArray())
            {
                var isSolution = post.TryGetProperty("isSolution", out var is_) && is_.GetBoolean();
                var isDevPost  = post.TryGetProperty("isDevPost",  out var id_) && id_.GetBoolean();
                var username   = post.TryGetProperty("username",   out var un) ? un.GetString() ?? "" : "";
                var text       = post.TryGetProperty("text",       out var tx2) ? tx2.GetString() ?? "" : "";
                var postUrl    = post.TryGetProperty("url",        out var pu) ? pu.GetString() : url;

                if (!isSolution && !isDevPost) continue;

                // Collect code blocks
                var codeBuilder = new System.Text.StringBuilder();
                if (post.TryGetProperty("codes", out var codes))
                    foreach (var c in codes.EnumerateArray())
                    {
                        var codeStr = c.GetString();
                        if (!string.IsNullOrWhiteSpace(codeStr))
                        {
                            codeBuilder.AppendLine(codeStr);
                            codeBuilder.AppendLine();
                        }
                    }

                var src = isSolution ? "forum-solution" : "forum-dev";
                pTitle.Value   = title;
                pContent.Value = text;
                pCode.Value    = codeBuilder.ToString().Trim();
                pSource.Value  = src;
                pUrl.Value     = postUrl ?? (object)DBNull.Value;
                pAuthor.Value  = username;
                pMeta.Value    = JsonSerializer.Serialize(new { solved, isSolution, isDevPost });
                await cmd.ExecuteNonQueryAsync(ct);
                count++;
            }
        }

        _logger.LogInformation("Indexed {Count} forum post entries from {File}", count, Path.GetFileName(path));
        return count;
    }

    private async Task<int> IndexMarkdownSections(
        string path, string source, SqliteTransaction tx, CancellationToken ct)
    {
        var lines = await File.ReadAllLinesAsync(path, ct);
        int count = 0;

        // Split by ## headings into sections
        var sections = SplitMarkdownSections(lines);

        var cmd = _db!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO practical_fts(title, content, code, source, url)
            VALUES ($title, $content, $code, $source, $url);
            """;
        var pTitle   = cmd.Parameters.Add("$title",   SqliteType.Text);
        var pContent = cmd.Parameters.Add("$content", SqliteType.Text);
        var pCode    = cmd.Parameters.Add("$code",    SqliteType.Text);
        var pSource  = cmd.Parameters.Add("$source",  SqliteType.Text);
        var pUrl     = cmd.Parameters.Add("$url",     SqliteType.Text);

        foreach (var (title, body) in sections)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body)) continue;

            // Extract URLs from body (first markdown link)
            var urlMatch = Regex.Match(body, @"\[.*?\]\((.+?)\)");
            var url = urlMatch.Success ? urlMatch.Groups[1].Value : null;

            // Separate code blocks from prose
            var (prose, code) = ExtractCodeBlocks(body);

            pTitle.Value   = title;
            pContent.Value = prose.Trim();
            pCode.Value    = code.Trim();
            pSource.Value  = source;
            pUrl.Value     = (object?)url ?? DBNull.Value;
            await cmd.ExecuteNonQueryAsync(ct);
            count++;
        }

        _logger.LogInformation("Indexed {Count} sections from {File}", count, Path.GetFileName(path));
        return count;
    }

    // ── FTS query builder ────────────────────────────────────────────────────

    /// <summary>
    /// Converts a user query string to a safe FTS5 MATCH expression.
    ///
    /// Single word     → "word*"          (prefix match)
    /// Multi-word AND  → "word1* word2*"  (both must appear — precise)
    /// Multi-word OR   → "word1* OR word2*" (any may appear — recall; BM25 still
    ///                   ranks documents matching more terms higher)
    /// Quoted phrase   → passed through as FTS5 phrase
    ///
    /// Strips FTS5 special operators that could cause parse errors.
    /// </summary>
    public static string BuildFtsQuery(string query, bool orMode = false)
    {
        query = query.Trim();
        if (string.IsNullOrEmpty(query)) return "";

        // If user already quotes something, treat as phrase search
        if (query.StartsWith('"') && query.EndsWith('"') && query.Length > 2)
            return query;

        // Sanitize: strip FTS5 operators/punctuation, keep alphanumeric + useful chars
        var clean = Regex.Replace(query, @"[""*\(\)\^]", " ");
        var terms = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToArray();

        if (terms.Length == 0) return "";

        if (terms.Length == 1)
            return terms[0] + "*";

        // Prefix wildcard on every term — "rot" should find "Rotation"
        var prefixed = terms.Select(t => t + "*").ToArray();
        return orMode
            ? string.Join(" OR ", prefixed)
            : string.Join(" ", prefixed);
    }

    // ── Internal parsing helpers ─────────────────────────────────────────────

    private static IEnumerable<(string Title, string Body)> SplitMarkdownSections(string[] lines)
    {
        var sections = new List<(string, string)>();
        string currentTitle = "";
        var bodyLines = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            if (line.StartsWith("## ") || line.StartsWith("### "))
            {
                if (bodyLines.Length > 0 || currentTitle.Length > 0)
                    sections.Add((currentTitle, bodyLines.ToString()));

                currentTitle = line.TrimStart('#', ' ');
                bodyLines.Clear();
            }
            else
            {
                bodyLines.AppendLine(line);
            }
        }

        if (bodyLines.Length > 0 || currentTitle.Length > 0)
            sections.Add((currentTitle, bodyLines.ToString()));

        return sections;
    }

    private static (string Prose, string Code) ExtractCodeBlocks(string text)
    {
        var proseBuilder = new System.Text.StringBuilder();
        var codeBuilder  = new System.Text.StringBuilder();

        var inCode    = false;
        var fenceChar = "";
        foreach (var line in text.Split('\n'))
        {
            if (!inCode && (line.TrimStart().StartsWith("```") || line.TrimStart().StartsWith("~~~")))
            {
                inCode    = true;
                fenceChar = line.TrimStart()[..3];
            }
            else if (inCode && line.TrimStart().StartsWith(fenceChar))
            {
                inCode = false;
            }
            else if (inCode)
            {
                codeBuilder.AppendLine(line);
            }
            else
            {
                proseBuilder.AppendLine(line);
            }
        }

        return (proseBuilder.ToString(), codeBuilder.ToString());
    }

    private async Task ExecuteAsync(string sql, SqliteTransaction? tx = null)
    {
        using var cmd = _db!.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Walk upward from the knowledge directory to find VVVVNodeAnalyzer/output/.
    /// In dev layout the binary is buried in src/*/bin/Debug/net8.0/ so we need
    /// to go up ~5 levels to reach the repo root.
    /// </summary>
    private static string? FindOutputDir(string knowledgeDir)
    {
        var dir = Directory.GetParent(knowledgeDir)?.FullName;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "VVVVNodeAnalyzer", "output");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    private void EnsureReady()
    {
        if (!IsReady || _db is null)
            throw new InvalidOperationException("SearchIndexService not initialized. Call InitializeAsync() first.");
    }

    public void Dispose()
    {
        _db?.Close();
        _db?.Dispose();
        _db = null;
        IsReady = false;
    }
}
