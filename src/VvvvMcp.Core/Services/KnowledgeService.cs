using Microsoft.Extensions.Logging;

namespace VvvvMcp.Core.Services;

/// <summary>
/// Loads and serves markdown knowledge files from the knowledge/ directory.
/// These files contain vvvv gamma concepts, patterns, package documentation, etc.
///
/// When a SearchIndexService is wired in (via SetSearchIndex), the Search()
/// method delegates to SQLite FTS5 for BM25-ranked results. Without it, the
/// original in-memory term-frequency search is used as fallback.
/// </summary>
public class KnowledgeService
{
    private readonly ILogger<KnowledgeService> _logger;
    private readonly Dictionary<string, KnowledgeFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private SearchIndexService? _searchIndex;
    private string? _knowledgeDir;

    public KnowledgeService(ILogger<KnowledgeService> logger)
    {
        _logger = logger;
    }

    public bool IsLoaded => _files.Count > 0;

    /// <summary>Wire in the FTS5 search index after it has been initialized.</summary>
    public void SetSearchIndex(SearchIndexService index) => _searchIndex = index;

    public async Task LoadAsync(string knowledgeDirectory, CancellationToken ct = default)
    {
        if (!Directory.Exists(knowledgeDirectory))
        {
            _logger.LogWarning("Knowledge directory not found: {Dir}", knowledgeDirectory);
            return;
        }

        _knowledgeDir = knowledgeDirectory;
        _files.Clear();

        var markdownFiles = Directory.GetFiles(knowledgeDirectory, "*.md", SearchOption.TopDirectoryOnly);

        foreach (var filePath in markdownFiles)
        {
            ct.ThrowIfCancellationRequested();
            var name        = Path.GetFileNameWithoutExtension(filePath);
            var content     = await File.ReadAllTextAsync(filePath, ct);
            var description = ExtractDescription(content, name);
            _files[name]    = new KnowledgeFile(name, filePath, description, content);
        }

        _logger.LogInformation("Loaded {Count} knowledge files from {Dir}", _files.Count, knowledgeDirectory);
    }

    /// <summary>All loaded knowledge files — used by SearchIndexService for bulk indexing.</summary>
    public IReadOnlyCollection<KnowledgeFile> GetAllFiles() => _files.Values;

    public IReadOnlyList<KnowledgeFileSummary> ListFiles()
    {
        return _files.Values
            .Select(f => new KnowledgeFileSummary(f.Name, f.Description))
            .OrderBy(f => f.Name)
            .ToList();
    }

    public KnowledgeFile? GetFile(string name) => _files.GetValueOrDefault(name);

    /// <summary>
    /// Full-text search over knowledge files.
    /// Uses SQLite FTS5 BM25 when the index is available; falls back to in-memory
    /// term-frequency scoring otherwise.
    /// </summary>
    public List<KnowledgeSearchResult> Search(string query, int limit = 5)
    {
        if (!IsLoaded || string.IsNullOrWhiteSpace(query)) return [];

        // Prefer FTS5 when available
        if (_searchIndex?.IsReady == true)
            return _searchIndex.SearchKnowledge(query, limit);

        return SearchInMemory(query, limit);
    }

    // ── In-memory fallback ────────────────────────────────────────────────────

    private List<KnowledgeSearchResult> SearchInMemory(string query, int limit)
    {
        var queryLower = query.ToLowerInvariant();
        var terms      = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var results    = new List<KnowledgeSearchResult>();

        foreach (var file in _files.Values)
        {
            double score      = 0;
            var nameLower     = file.Name.ToLowerInvariant();
            var descLower     = file.Description.ToLowerInvariant();
            var contentLower  = file.Content.ToLowerInvariant();

            if (nameLower.Contains(queryLower))  score += 50;
            if (descLower.Contains(queryLower))  score += 30;

            int contentMatches = 0;
            foreach (var term in terms)
            {
                int idx = 0;
                while ((idx = contentLower.IndexOf(term, idx)) >= 0)
                {
                    contentMatches++;
                    idx += term.Length;
                    if (contentMatches > 20) break;
                }
            }
            score += Math.Min(contentMatches * 2, 40);

            if (score > 0)
            {
                var snippet = ExtractSnippet(file.Content, queryLower, 300);
                results.Add(new KnowledgeSearchResult(file.Name, file.Description, snippet, score));
            }
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(limit)
            .ToList();
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    private static string ExtractDescription(string content, string name)
    {
        if (content.StartsWith("---"))
        {
            var end = content.IndexOf("\n---", 4);
            if (end > 0)
            {
                var frontmatter = content[4..end];
                var descMatch = System.Text.RegularExpressions.Regex.Match(
                    frontmatter, @"^description:\s*[""']?(.+?)[""']?\s*$",
                    System.Text.RegularExpressions.RegexOptions.Multiline);
                if (descMatch.Success)
                    return descMatch.Groups[1].Value.Trim('"', '\'').Trim();
            }
        }

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim('#', ' ').Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("---"))
                return trimmed.Length > 120 ? trimmed[..120] + "..." : trimmed;
        }

        return name;
    }

    private static string ExtractSnippet(string content, string query, int maxLength)
    {
        var idx = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return content.Length > maxLength ? content[..maxLength] + "..." : content;

        var start   = Math.Max(0, idx - 100);
        var end     = Math.Min(content.Length, idx + maxLength - 100);
        var snippet = content[start..end];
        if (start > 0)             snippet = "..." + snippet;
        if (end < content.Length)  snippet += "...";
        return snippet;
    }
}

public record KnowledgeFile(
    string Name,
    string FilePath,
    string Description,
    string Content
);

public record KnowledgeFileSummary(
    string Name,
    string Description
);

public record KnowledgeSearchResult(
    string Name,
    string Description,
    string Snippet,
    double Score
);
