using Microsoft.Extensions.Logging;
using VvvvMcp.Core.Models;

namespace VvvvMcp.Core.Services;

/// <summary>
/// A node resolved against either the live vvvv instance (ground truth: real pin
/// types from the NodeFactoryRegistry) or the offline catalog (fallback, may have
/// "Object" pin types). Carries everything needed to serialize the node into XML.
/// </summary>
public record ResolvedNode(
    string Name,
    string Category,
    string FullName,
    string Kind,            // "Process" | "Operation"
    string Package,         // NuGet package id, e.g. "VL.Stride" ("" if unknown)
    string DependencyFile,  // .vl file hint for LastDependency, e.g. "VL.Stride.Engine.vl"
    List<NodePin> Inputs,
    List<NodePin> Outputs,
    string Origin           // "live" | "catalog"
)
{
    /// <summary>XML Choice kind for the node reference.</summary>
    public string XmlNodeKind => Kind == "Process" ? "ProcessAppFlag" : "OperationCallFlag";
}

/// <summary>
/// Resolves node names/categories to full node descriptions.
/// Live bridge first (exact pins + types), offline catalog as fallback.
/// </summary>
public class NodeResolutionService
{
    private readonly BridgeClientService _bridge;
    private readonly NodeCatalogService _catalog;
    private readonly ILogger<NodeResolutionService> _logger;

    public NodeResolutionService(
        BridgeClientService bridge,
        NodeCatalogService catalog,
        ILogger<NodeResolutionService> logger)
    {
        _bridge = bridge;
        _catalog = catalog;
        _logger = logger;
    }

    /// <summary>
    /// Resolve a node by name (+ optional category/package hint).
    /// Returns null when not found anywhere; check Suggestions on the result wrapper.
    /// </summary>
    public async Task<NodeResolutionResult> ResolveAsync(
        string name, string? category = null, string? package = null, CancellationToken ct = default)
    {
        // ── 1. Live registry (ground truth) ──────────────────────────────────
        if (await _bridge.CheckAvailabilityAsync())
        {
            try
            {
                var live = await _bridge.LookupLiveNodeAsync(name, category);
                if (live is not null && live.Found && live.Nodes.Count > 0)
                {
                    var node = PickBest(live.Nodes, category, package);
                    return NodeResolutionResult.Ok(FromLive(node));
                }
                if (live is not null && !live.Found && live.Suggestions.Count > 0)
                {
                    // Live says no — catalog may still know it (pack not loaded live).
                    var fromCatalog = ResolveFromCatalog(name, category, package);
                    if (fromCatalog is not null)
                        return NodeResolutionResult.Ok(fromCatalog);
                    return NodeResolutionResult.NotFound(live.Suggestions);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Live node lookup failed for {Name}", name);
            }
        }

        // ── 2. Offline catalog fallback ──────────────────────────────────────
        var resolved = ResolveFromCatalog(name, category, package);
        if (resolved is not null)
            return NodeResolutionResult.Ok(resolved);

        return NodeResolutionResult.NotFound(CatalogSuggestions(name));
    }

    private ResolvedNode? ResolveFromCatalog(string name, string? category, string? package)
    {
        if (!_catalog.IsLoaded) return null;

        var matches = _catalog.FindTolerant(name);
        if (matches.Count == 0) return null;

        var node = matches.Count == 1
            ? matches[0]
            : matches.FirstOrDefault(n =>
                    category is not null &&
                    n.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
              ?? matches.FirstOrDefault(n =>
                    category is not null &&
                    n.Category.StartsWith(category + ".", StringComparison.OrdinalIgnoreCase))
              ?? matches.FirstOrDefault(n =>
                    package is not null &&
                    n.Package.Equals(package, StringComparison.OrdinalIgnoreCase))
              // Prefer the "richest" description (most pins, real types)
              ?? matches
                    .OrderByDescending(n => n.Inputs.Count + n.Outputs.Count)
                    .ThenByDescending(n => n.Inputs.Count(p => p.Type != "Object"))
                    .First();

        var kind = node.Type is NodeType.Process or NodeType.Class || node.HasState
            ? "Process"
            : "Operation";

        return new ResolvedNode(
            node.Name,
            node.Category,
            node.FullName,
            kind,
            node.Package,
            DependencyFile: node.Package.EndsWith(".vl", StringComparison.OrdinalIgnoreCase)
                ? node.Package
                : node.Package + ".vl",
            node.Inputs,
            node.Outputs,
            "catalog");
    }

    private List<string> CatalogSuggestions(string name)
    {
        if (!_catalog.IsLoaded) return [];
        try
        {
            return _catalog.Search(name, limit: 8)
                .Select(r => r.Node.FullName)
                .ToList();
        }
        catch { return []; }
    }

    private static LiveNodeInfo PickBest(List<LiveNodeInfo> nodes, string? category, string? package)
    {
        return
            // 1. Exact category match ("Stride" beats "Stride.API.Engine.SceneInstance - Advanced")
            nodes.FirstOrDefault(n =>
                category is not null &&
                n.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            // 2. Category prefix match
            ?? nodes.FirstOrDefault(n =>
                category is not null &&
                n.Category.StartsWith(category + ".", StringComparison.OrdinalIgnoreCase))
            // 3. Package match
            ?? nodes.FirstOrDefault(n =>
                package is not null &&
                n.Package.Equals(package, StringComparison.OrdinalIgnoreCase))
            // 4. Richest description
            ?? nodes.OrderByDescending(n => n.Inputs.Count + n.Outputs.Count).First();
    }

    private static ResolvedNode FromLive(LiveNodeInfo n)
    {
        return new ResolvedNode(
            n.Name,
            n.Category,
            n.FullName,
            n.Kind,
            n.Package,
            DependencyFile: !string.IsNullOrEmpty(n.SourceFile)
                ? Path.GetFileName(n.SourceFile)
                : (string.IsNullOrEmpty(n.Package) ? "" : n.Package + ".vl"),
            n.Inputs.Select(p => new NodePin(p.Name, p.Type, DefaultValue: p.DefaultValue, IsOptional: p.Optional, IsHidden: p.Hidden, IsState: p.State, IsPinGroup: p.IsPinGroup)).ToList(),
            n.Outputs.Select(p => new NodePin(p.Name, p.Type, DefaultValue: p.DefaultValue, IsOptional: p.Optional, IsHidden: p.Hidden, IsState: p.State, IsPinGroup: p.IsPinGroup)).ToList(),
            "live");
    }
}

public record NodeResolutionResult(
    ResolvedNode? Node,
    List<string> Suggestions)
{
    public bool Found => Node is not null;
    public static NodeResolutionResult Ok(ResolvedNode node) => new(node, []);
    public static NodeResolutionResult NotFound(List<string> suggestions) => new(null, suggestions);
}
