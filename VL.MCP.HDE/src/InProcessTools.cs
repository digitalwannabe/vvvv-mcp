using Microsoft.Extensions.Logging.Abstractions;
using VL.Core;
using VvvvMcp.Core.Services;

namespace VL.MCP;

/// <summary>
/// Routes MCP tool calls from the in-vvvv SSE server (chat mode) to the shared
/// VvvvMcp.Core services — the SAME implementations the external stdio MCP server uses.
/// This is what gives chat mode build_patch, live node search, and the file tools.
///
/// Live node resolution inside vvvv goes through a loopback BridgeClientService to the
/// bridge's own /api/nodes endpoint (the LiveNodeCatalog), so there is exactly one
/// source of truth. The offline SQLite catalog is intentionally not used in-process.
/// </summary>
internal sealed class InProcessTools
{
    private readonly PatchReaderService _reader = new(NullLogger<PatchReaderService>.Instance);
    private readonly PatchWriterService _writer = new(NullLogger<PatchWriterService>.Instance);
    private readonly NodeCatalogService _catalog = new(NullLogger<NodeCatalogService>.Instance); // stays unloaded → live-only
    private readonly PatchExplainerService _explainer;
    private readonly BridgeClientService _loopback = new(NullLogger<BridgeClientService>.Instance);
    private readonly NodeResolutionService _resolver;
    private readonly PatchBuilderService _builder;

    public InProcessTools()
    {
        _explainer = new PatchExplainerService(NullLogger<PatchExplainerService>.Instance, _catalog);
        _resolver = new NodeResolutionService(_loopback, _catalog, NullLogger<NodeResolutionService>.Instance);
        _builder = new PatchBuilderService(_writer, _resolver, _loopback, NullLogger<PatchBuilderService>.Instance);
    }

    /// <summary>Point the loopback client at the bridge's actual port.</summary>
    public void SetBridgePort(int port) => _loopback.SetPort(port);

    public async Task<object> DispatchAsync(string toolName, string paramsJson)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(
            string.IsNullOrWhiteSpace(paramsJson) ? "{}" : paramsJson);
        var p = doc.RootElement;

        string Str(string key, string def = "") =>
            p.TryGetProperty(key, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
                ? v.GetString() ?? def : def;

        switch (toolName)
        {
            // ── The primary write path ────────────────────────────────────────
            case "build_patch":
                return await _builder.BuildAsync(Str("spec"));

            // ── Live node catalog (loopback to own /api/nodes) ────────────────
            case "search_nodes_live":
            {
                var r = await _loopback.SearchLiveNodesAsync(Str("query"), NullIfEmpty(Str("category")), 20, false);
                return r is null ? new { error = "live node endpoint unavailable" } : r;
            }
            case "get_node_details_live":
            {
                var r = await _loopback.LookupLiveNodeAsync(Str("name"), NullIfEmpty(Str("category")));
                return r is null ? new { error = "live node endpoint unavailable" } : r;
            }
            case "refresh_live_nodes":
            {
                var r = await _loopback.RefreshLiveNodesAsync();
                return r is null ? new { error = "live node endpoint unavailable" } : r;
            }

            // ── Patch read ────────────────────────────────────────────────────
            case "read_patch":
            {
                var patch = _reader.ReadPatch(Str("filePath"));
                return new
                {
                    file = System.IO.Path.GetFileName(Str("filePath")),
                    documentId = patch.DocumentId,
                    languageVersion = patch.LanguageVersion,
                    dependencies = patch.Dependencies.Select(d => new { d.Location, d.Version }),
                    nodes = patch.AllNodes.Select(n => new
                    {
                        n.Id,
                        name = n.Reference.NodeName ?? n.Name,
                        category = n.Reference.LastCategoryFullName,
                        kind = n.Reference.Kind,
                        n.Bounds,
                        pins = n.Pins.Where(pp => !pp.IsHidden).Select(pp => new { pp.Name, pp.Kind, pp.DefaultValue })
                    }),
                    pads = patch.AllPads.Select(pp => new { pp.Id, type = pp.TypeName, pp.Value }),
                    connections = patch.Links.Select(l => new { l.SourceId, l.TargetId }),
                    stats = new { nodeCount = patch.AllNodes.Count, padCount = patch.AllPads.Count, linkCount = patch.Links.Count }
                };
            }
            case "explain_patch":
            {
                var patch = _reader.ReadPatch(Str("filePath"));
                return new { explanation = _explainer.ExplainPatch(patch, Str("filePath")) };
            }
            case "list_patch_dependencies":
            {
                var patch = _reader.ReadPatch(Str("filePath"));
                return new { dependencies = patch.Dependencies.Select(d => new { d.Location, d.Version }), count = patch.Dependencies.Count };
            }

            default:
                return new { error = $"Unknown in-process tool: {toolName}" };
        }
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
