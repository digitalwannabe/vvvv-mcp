namespace VvvvMcp.Core.Models;

/// <summary>
/// Matches the top-level structure of vvvv_nodes_mcp.json produced by UsableNodesExporter.
/// </summary>
public record NodeCatalog(
    string LibraryName,
    string Version,
    string Description,
    string ExtractionDate,
    List<VvvvNode> Nodes,
    List<string> Categories,
    int TotalNodes,
    Dictionary<string, int> NodesByType
);

public record NodeSearchResult(
    VvvvNode Node,
    double Score
);

public record CatalogStats(
    int TotalNodes,
    int TotalPackages,
    int TotalCategories,
    List<string> TopCategories,
    Dictionary<string, int> NodesByPackage,
    Dictionary<string, int> NodesByType
);

/// <summary>
/// A result from the practical knowledge FTS index (help patches, forum, code snippets).
/// </summary>
public record PracticalSearchResult(
    string Source,    // 'help-patch' | 'forum-solution' | 'forum-dev' | 'forum-snippet' | etc.
    string Title,     // topic title or node name
    string Snippet,   // prose snippet around best match
    string CodeSnippet, // code snippet around best match (may be empty)
    string? Url,      // source URL, if available
    string? Author,   // username, if forum post
    double Score      // BM25 relevance (higher = better)
);
