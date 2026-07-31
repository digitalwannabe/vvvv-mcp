using System.ComponentModel;
using ModelContextProtocol.Server;
using VvvvMcp.Core.Services;

namespace VvvvMcp.Tools;

[McpServerToolType]
public class NodeCatalogTools
{
    private readonly NodeCatalogService _catalog;

    public NodeCatalogTools(NodeCatalogService catalog)
    {
        _catalog = catalog;
    }

    [McpServerTool(Name = "search_nodes")]
    [Description("Search the vvvv gamma node catalog by name, category, or description. Returns matching nodes sorted by relevance.")]
    public object SearchNodes(
        [Description("Search query — node name, category, or keyword (e.g. 'Transform', 'Math', 'oscillator', 'Box')")] string query,
        [Description("Optional: filter by category prefix (e.g. '3D.Transform', 'Math', 'Stride')")] string? category = null,
        [Description("Maximum results to return (default 25, max 100)")] int limit = 25)
    {
        if (!_catalog.IsLoaded)
            return new { error = "Node catalog not loaded. Set VVVV_MCP_CATALOG environment variable." };

        limit = Math.Clamp(limit, 1, 100);
        var results = _catalog.Search(query, category, limit);

        return new
        {
            query,
            category,
            resultCount = results.Count,
            nodes = results.Select(r => new
            {
                r.Node.Name,
                r.Node.FullName,
                r.Node.Category,
                r.Node.Type,
                r.Node.Source,
                r.Node.Summary,
                r.Node.IsGeneric,
                r.Node.HasState,
                r.Node.Tags,
                inputs = r.Node.Inputs.Select(p => new { p.Name, p.Type, p.DefaultValue, p.IsOptional }),
                outputs = r.Node.Outputs.Select(p => new { p.Name, p.Type }),
                score = Math.Round(r.Score, 1)
            })
        };
    }

    [McpServerTool(Name = "get_node_details")]
    [Description("Get full details of a specific vvvv node by its exact name. Returns all matching nodes (there may be multiple with the same name in different packages).")]
    public object GetNodeDetails(
        [Description("Exact node name (e.g. 'TransformSRT', 'Box', '+', 'LFO')")] string name)
    {
        if (!_catalog.IsLoaded)
            return new { error = "Node catalog not loaded." };

        var nodes = _catalog.GetByName(name);
        if (!nodes.Any())
            return new { error = $"No node found with name '{name}'.", suggestion = "Try search_nodes to find similar nodes." };

        return new
        {
            name,
            matchCount = nodes.Count,
            nodes = nodes.Select(n => new
            {
                n.Name,
                n.FullName,
                n.Category,
                n.Type,
                n.Source,
                n.Summary,
                n.Remarks,
                n.Tags,
                n.IsGeneric,
                n.HasState,
                n.Inputs,
                n.Outputs
            })
        };
    }

    [McpServerTool(Name = "list_categories")]
    [Description("List all node categories in the vvvv node catalog. Optionally filter by prefix.")]
    public object ListCategories(
        [Description("Optional category prefix to filter (e.g. '3D', 'Stride', 'Math')")] string? prefix = null)
    {
        if (!_catalog.IsLoaded)
            return new { error = "Node catalog not loaded." };

        var categories = _catalog.GetCategories(prefix);
        return new
        {
            prefix,
            count = categories.Count,
            categories
        };
    }

    [McpServerTool(Name = "list_packages")]
    [Description("List all available vvvv packages/libraries in the node catalog.")]
    public object ListPackages()
    {
        if (!_catalog.IsLoaded)
            return new { error = "Node catalog not loaded." };

        var packages = _catalog.GetPackages();
        return new
        {
            count = packages.Count,
            packages
        };
    }
}
