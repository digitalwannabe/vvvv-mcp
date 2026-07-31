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
