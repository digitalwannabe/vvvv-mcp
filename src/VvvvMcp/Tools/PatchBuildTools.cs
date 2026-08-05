using System.ComponentModel;
using ModelContextProtocol.Server;
using VvvvMcp.Core.Services;

namespace VvvvMcp.Tools;

[McpServerToolType]
public class PatchBuildTools
{
    private readonly PatchBuilderService _builder;

    public PatchBuildTools(PatchBuilderService builder)
    {
        _builder = builder;
    }

    [McpServerTool(Name = "build_patch")]
    [Description(
        "Build a whole connected subgraph in ONE call — the primary way to create patch content. " +
        "Resolves every node against the live vvvv registry (exact pins + types) or the offline catalog, " +
        "adds missing NuGet dependencies, declares all pins, auto-layouts by dataflow, wires all links " +
        "(pin groups auto-index: 'Child' → 'Child 2'), sets values, saves once, reloads in vvvv and reports compile errors. " +
        "Spec is a JSON object: { filePath, nodes: [{ key, name, category?, package?, kind?, bounds?, values? {pin:val} }], " +
        "pads: [{ key, type, value?, bounds? }], links: [{ from, to }], verify?, open?, verbosity? }. " +
        "Link endpoints: 'key.Pin Name', 'key' (first output/input), or an existing 22-char pin id from read_patch " +
        "— so a new subgraph can be wired INTO the existing patch in the same call. " +
        "Nodes are looked up by name; add 'category' when ambiguous (e.g. 'Box' in 'Stride.Models'). " +
        "Example: { filePath: 'C:/x/foo.vl', nodes: [ {key:'lfo', name:'LFO', category:'Animation'}, {key:'box', name:'Box', category:'Stride.Models'} ], links: [ {from:'lfo.Output', to:'box.Transformation'} ] }")]
    public async Task<object> BuildPatch(
        [Description("JSON build spec (see tool description). nodes:[] with only links is valid — batch-connects existing pins.")] string spec)
    {
        try
        {
            return await _builder.BuildAsync(spec);
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"build_patch failed: {ex.Message}" };
        }
    }
}
