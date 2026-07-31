using Microsoft.Extensions.Logging;

namespace VvvvMcp.Core.Services;

/// <summary>
/// Loads and serves markdown knowledge files from the knowledge/ directory.
/// These files contain vvvv gamma concepts, patterns, package documentation, etc.
/// </summary>
public class KnowledgeService
{
    private readonly ILogger<KnowledgeService> _logger;
    private readonly Dictionary<string, KnowledgeFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private string? _knowledgeDir;

    public KnowledgeService(ILogger<KnowledgeService> logger)
    {
        _logger = logger;
    }

    public bool IsLoaded => _files.Count > 0;

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
            var name = Path.GetFileNameWithoutExtension(filePath);
            var content = await File.ReadAllTextAsync(filePath, ct);
            var description = ExtractDescription(content, name);
            _files[name] = new KnowledgeFile(name, filePath, description, content);
        }

        _logger.LogInformation("Loaded {Count} knowledge files from {Dir}", _files.Count, knowledgeDirectory);
    }

    public IReadOnlyList<KnowledgeFileSummary> ListFiles()
    {
        return _files.Values
            .Select(f => new KnowledgeFileSummary(f.Name, f.Description))
            .OrderBy(f => f.Name)
            .ToList();
    }

    public KnowledgeFile? GetFile(string name)
    {
        return _files.GetValueOrDefault(name);
    }

    public List<KnowledgeSearchResult> Search(string query, int limit = 5)
    {
        if (!IsLoaded || string.IsNullOrWhiteSpace(query)) return [];

        var queryLower = query.ToLowerInvariant();
        var terms = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var results = new List<KnowledgeSearchResult>();

        foreach (var file in _files.Values)
        {
            double score = 0;
            var nameLower = file.Name.ToLowerInvariant();
            var descLower = file.Description.ToLowerInvariant();
            var contentLower = file.Content.ToLowerInvariant();

            if (nameLower.Contains(queryLower)) score += 50;
            if (descLower.Contains(queryLower)) score += 30;

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
                // Extract surrounding context of best match
                var snippet = ExtractSnippet(file.Content, queryLower, 300);
                results.Add(new KnowledgeSearchResult(file.Name, file.Description, snippet, score));
            }
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(limit)
            .ToList();
    }

    private static string ExtractDescription(string content, string name)
    {
        // Try YAML frontmatter description field
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

        // Fall back to first non-empty line after any heading
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

        var start = Math.Max(0, idx - 100);
        var end = Math.Min(content.Length, idx + maxLength - 100);
        var snippet = content[start..end];
        if (start > 0) snippet = "..." + snippet;
        if (end < content.Length) snippet += "...";
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
