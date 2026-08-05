using System.ComponentModel;
using ModelContextProtocol.Server;
using VvvvMcp.Core.Services;

namespace VvvvMcp.Tools;

[McpServerToolType]
public class NodeCatalogTools
{
    private readonly NodeCatalogService _catalog;
    private readonly BridgeClientService _bridge;

    public NodeCatalogTools(NodeCatalogService catalog, BridgeClientService bridge)
    {
        _catalog = catalog;
        _bridge = bridge;
    }

    [McpServerTool(Name = "search_nodes_live")]
    [Description("Search the LIVE node registry of the running vvvv instance — ground truth with exact pin names and real types. " +
        "Prefer this over search_nodes when the bridge is connected. Returns nodes the user can actually place right now.")]
    public async Task<object> SearchNodesLive(
        [Description("Search query — node name, category, or keyword (e.g. 'Transform', 'Box', 'oscillator')")] string query,
        [Description("Optional: filter by category prefix (e.g. '3D.Transform', 'Stride')")] string? category = null,
        [Description("Maximum results (default 20, max 100)")] int limit = 20,
        [Description("Include full pin lists with types (bigger response, default false)")] bool includePins = false)
    {
        if (!await _bridge.CheckAvailabilityAsync())
            return new { error = "Bridge not connected — use search_nodes (offline catalog) instead." };

        var result = await _bridge.SearchLiveNodesAsync(query, category, Math.Clamp(limit, 1, 100), includePins);
        if (result is null)
            return new { error = "Live node endpoint not available — the bridge in vvvv needs an update (VL.MCP.HDE ≥ 0.3). Falling back recommended: search_nodes." };

        return new
        {
            query,
            category,
            total = result.Total,
            count = result.Count,
            nodes = result.Nodes.Select(n => includePins
                ? (object)new
                {
                    n.Name, n.FullName, n.Category, n.Kind, n.Package,
                    inputs = n.Inputs.Select(p => new { p.Name, p.Type, p.DefaultValue }),
                    outputs = n.Outputs.Select(p => new { p.Name, p.Type })
                }
                : new
                {
                    n.Name, n.FullName, n.Category, n.Kind, n.Package
                })
        };
    }

    [McpServerTool(Name = "get_node_details_live")]
    [Description("Get exact pin names, real pin types and defaults for a node from the LIVE vvvv registry. " +
        "Use before wiring when unsure about pin names. Falls back gracefully with suggestions when not found.")]
    public async Task<object> GetNodeDetailsLive(
        [Description("Exact node name (e.g. 'TransformSRT', 'Box', 'LFO')")] string name,
        [Description("Optional category hint to disambiguate (e.g. 'Stride.Models')")] string? category = null)
    {
        if (!await _bridge.CheckAvailabilityAsync())
            return new { error = "Bridge not connected — use get_node_details (offline catalog) instead." };

        var result = await _bridge.LookupLiveNodeAsync(name, category);
        if (result is null)
            return new { error = "Live node endpoint not available — update the bridge (VL.MCP.HDE ≥ 0.3)." };

        if (!result.Found)
            return new { found = false, name, suggestions = result.Suggestions };

        return new
        {
            found = true,
            matchCount = result.MatchCount,
            nodes = result.Nodes.Select(n => new
            {
                n.Name,
                n.FullName,
                n.Category,
                n.Kind,
                n.Package,
                n.SourceFile,
                inputs = n.Inputs.Select(p => new { p.Name, p.Type, p.DefaultValue, p.IsPinGroup }),
                outputs = n.Outputs.Select(p => new { p.Name, p.Type, p.IsPinGroup })
            })
        };
    }

    [McpServerTool(Name = "refresh_live_nodes")]
    [Description("Rebuild the live node snapshot inside vvvv (e.g. after installing a new NuGet pack). Returns node count.")]
    public async Task<object> RefreshLiveNodes()
    {
        if (!await _bridge.CheckAvailabilityAsync())
            return new { error = "Bridge not connected." };

        var stats = await _bridge.RefreshLiveNodesAsync();
        if (stats is null)
            return new { error = "Live node endpoint not available — update the bridge (VL.MCP.HDE ≥ 0.3)." };

        return new
        {
            success = true,
            message = "Live node snapshot rebuild requested; it completes within a few frames.",
            stats.Nodes,
            stats.LastError
        };
    }

    [McpServerTool(Name = "search_nodes")]
    [Description("Search the OFFLINE vvvv node catalog by name, category, or keyword. Compact candidate list (no pins — call get_node_details for pins). Prefer search_nodes_live when the bridge is up (exact pins + real types).")]
    public object SearchNodes(
        [Description("Search query — node name, category, or keyword (e.g. 'Transform', 'Math', 'oscillator', 'Box')")] string query,
        [Description("Optional: filter by category prefix (e.g. '3D.Transform', 'Math', 'Stride')")] string? category = null,
        [Description("Maximum results to return (default 25, max 100)")] int limit = 25)
    {
        if (!_catalog.IsLoaded)
            return new { error = "Node catalog not loaded." };

        limit = Math.Clamp(limit, 1, 100);
        var results = _catalog.Search(query, category, limit);

        // Compact: pins live on get_node_details — search is for finding candidates.
        // Token efficiency matters here (this result is read before every patch build).
        string? hint = null;
        if (results.Count == 0)
            hint = "No offline matches. Try search_nodes_live (bridge), a shorter query, or a category filter.";
        else if (results.Any(r => r.Node.Inputs.Any(p => p.Type == "Object")))
            hint = "Offline catalog: pin types may be 'Object'. get_node_details_live (bridge) has exact pins + real types.";

        return new
        {
            query,
            category,
            resultCount = results.Count,
            hint,
            nodes = results.Select(r => new
            {
                r.Node.Name,
                r.Node.FullName,
                r.Node.Category,
                r.Node.Type,
                r.Node.Source,
                r.Node.Summary,
                score = Math.Round(r.Score, 1)
            })
        };
    }

    [McpServerTool(Name = "get_node_details")]
    [Description("Get full details of a specific vvvv node by name. Tolerant: accepts exact names, full names ('Stride.Models.Box'), and variant forms ('Rotation (Successive)'). Returns all matching nodes (there may be multiple with the same name in different packages).")]
    public object GetNodeDetails(
        [Description("Node name (e.g. 'TransformSRT', 'Box', 'LFO', 'Rotation (Successive)')")] string name)
    {
        if (!_catalog.IsLoaded)
            return new { error = "Node catalog not loaded." };

        var nodes = _catalog.FindTolerant(name);
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
