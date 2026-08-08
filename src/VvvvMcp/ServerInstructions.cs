namespace VvvvMcp;

/// <summary>
/// Tier-0 knowledge: the MCP server instructions, injected once at initialize.
/// This is the "pointed start" — the golden workflow + the rules that prevent the
/// most failures + a map of when to read which knowledge file. Keep it DENSE:
/// every token here lives in the model's context for the whole session.
///
/// The full tiered model:
///   Tier 0  this string (once, always on)
///   Tier 1  tool descriptions (per-turn, behavioral)
///   Tier 2  conditional hints inside tool results (only when relevant)
///   Tier 3  search_knowledge / search_practical (long tail, on demand)
///   Tier 4  read_knowledge full files (deep dives only)
/// </summary>
internal static class ServerInstructions
{
    public const string Text =
        "vvvv gamma MCP. VL = visual .NET dataflow; .vl files are XML; programs run live at ~60fps.\n" +
        "WORKFLOW: prefer build_patch — one call builds a connected subgraph (nodes+pads+links+deps+layout+verify) " +
        "with pins/types resolved from the live vvvv registry. Use add_node/connect_pins only for trivial single edits. " +
        "Wire into an existing patch by passing existing pin IDs (from read_patch) as link endpoints.\n" +
        "RULES: Link Ids=\"outputId,inputId\" (source first). NugetDependency is a child of Document, after </Patch>. " +
        "Node Bounds height=19. Stateful process nodes=ProcessAppFlag, stateless operations=OperationCallFlag. " +
        "Pin groups auto-index (Child→Child 2). State outputs and Node Context are hidden by default.\n" +
        "NODES: search_nodes_live/get_node_details_live when the bridge is up (exact pins+real types); " +
        "search_nodes/get_node_details offline otherwise (types may be 'Object' — trust names). " +
        "Add a category hint when a name is ambiguous ('Box' in 'Stride.Models').\n" +
        "MINIMALISM: vvvv pins have sensible defaults — do NOT add nodes that just set defaults. " +
        "Unconnected pins keep their default (e.g. Material=default PBR, Color=white). " +
        "Never insert * by 1 or + 0 as 'speed control' — LFO.Period IS the speed control. " +
        "Prefer the fewest nodes that achieve the goal; the user can add tweaking nodes later.\n" +
        "ROTATION: for rotating objects over time use 'Rotation (Successive) [3D.Transform]' (process node, " +
        "feed Angular Delta as Vector3, e.g. values {\"Angular Delta\":\"0.01, 0, 0\"} for X-axis spin). " +
        "For one-shot rotation matrix use 'Rotation [3D.Matrix]' (has Pitch/Yaw/Roll in cycles, 0-1=full turn).\n" +
        "KNOWLEDGE (read once, only when the task needs it): vl-quickref=orientation; " +
        "vl-common-graphs=pin-level patterns (Stride scene, Skia, channels, TextureFX, Fuse); " +
        "vl-building-blocks=definitions/regions/pads/XML; vl-project-architecture=multi-doc projects; " +
        "vl-file-format=full XML spec; vvvv-internals-advanced=bridge/reflection (advanced). " +
        "Use search_knowledge/search_practical for the long tail instead of reading whole files.\n" +
        "VERIFY: build_patch already reloads + reports compile errors for the document. " +
        "After manual edits call get_vvvv_errors(filePath). Errors carry elementId == the XML Id of the faulty node.";
}
