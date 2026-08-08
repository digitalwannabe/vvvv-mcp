using System.ComponentModel;
using ModelContextProtocol.Server;
using VvvvMcp.Core.Services;

namespace VvvvMcp.Prompts;

[McpServerPromptType]
public class VvvvPrompts
{
    private readonly KnowledgeService _knowledge;

    public VvvvPrompts(KnowledgeService knowledge)
    {
        _knowledge = knowledge;
    }

    // ── Patterns resource instruction ─────────────────────────────────────────
    //
    // NOT inlined. The model reads the resource ONCE and it stays in the
    // context window for the whole session. If the user invokes the same prompt
    // again, the instruction tells the model to check its context first and
    // skip the re-read — avoiding repeated injection of 700 lines.

    private const string PatternsInstruction =
        "**Step 0 — patterns reference (read once per session, skip if already in context):**\n" +
        "Read the resource `vvvv://knowledge/patterns`. It contains verified XML for: " +
        "all IOBox types (Float32, Boolean bang/toggle, String comment, Vector3, RGBA, Spread<T>), " +
        "If/ForEach region structure with ControlPoint splicers and wiring, and common process " +
        "nodes (LFO, S+H, Changed, Switch, OnOpen, Damper). " +
        "For Stride/Skia/Fuse/channel graphs read `vl-common-graphs`; " +
        "for definitions/channels/reactive/C# interop read `vl-building-blocks`. " +
        "If this resource is already visible earlier in the conversation, do not " +
        "read it again — use what is already in your context.";

    // ── Prompts ───────────────────────────────────────────────────────────────

    [McpServerPrompt(Name = "edit_vl_patch")]
    [Description("Get guidance for editing or extending an existing vvvv gamma .vl patch.")]
    public string EditVlPatch(
        [Description("Absolute path to the .vl patch file to edit")] string filePath,
        [Description("What to add, change, or remove in the patch")] string task)
    {
        return
            $"Edit the vvvv gamma patch at:\n{filePath}\n\nTask: {task}\n\n" +
            PatternsInstruction + "\n\n" +
            $"""
## Edit workflow

1. **Read the patch first**: call `read_patch("{filePath}")` to understand the
   current node IDs, pin IDs, existing connections, and language version.
   All new links must reference IDs that exist in the patch.

2. **Build the change in ONE call with `build_patch`** — nodes + pads + links +
   dependencies + verification in a single shot. Nodes are resolved against the
   LIVE vvvv registry (exact pins, real types, correct hidden pins); missing
   NuGet dependencies are added automatically; the result is reloaded in vvvv
   and compile errors are reported back.
   - Link endpoints accept `key.Pin Name` for new nodes AND raw pin IDs from
     read_patch for existing nodes — so you can wire the new subgraph INTO the
     existing patch in the same call.
   - Pin groups auto-index: linking `scene.Child` twice creates Child + Child 2.
   - Only fall back to `add_node`/`connect_pins` for single trivial additions.

3. **Common graphs**: `read_knowledge("vl-common-graphs")` has pin-level link
   structure for the recurring patterns (Stride scene, Skia layers, channels,
   TextureFX, Fuse particles…). Prefer these over inventing wiring.

4. **Preserve existing structure**: do NOT change existing node/pad IDs.
   Removing nodes also removes their links.

5. **Verify**: build_patch already reloads + reports compile errors. If you made
   manual edits, call `get_vvvv_errors("{filePath}")` to check just this document.
""";
    }

    [McpServerPrompt(Name = "create_vl_patch")]
    [Description("Get guidance for creating a new vvvv gamma .vl patch from scratch.")]
    public string CreateVlPatch(
        [Description("Description of what the patch should do")] string description)
    {
        return
            $"Create a vvvv gamma .vl patch that does:\n\"{description}\"\n\n" +
            PatternsInstruction + "\n\n" +
            """
## Creation workflow

1. **Pick a known graph**: `read_knowledge("vl-common-graphs")` — the recurring
   subgraphs with exact node names, categories and pin-level links (Stride scene,
   Skia paint→layer→renderer, channel write/read, TextureFX chains, Fuse
   particles…). Starting from one of these is faster and correct-er than
   assembling from scratch.

2. **Build with `build_patch`** — ONE call creates the whole connected subgraph:
   resolves nodes against the live vvvv registry (exact pins + real types),
   adds NuGet dependencies, declares pins with correct visibility, auto-layouts
   by dataflow, wires links, saves, reloads in vvvv, and reports compile errors.
   Node lookup needs just `name` (+ `category` when ambiguous, e.g. "Box" in
   "Stride.Models"). If a node can't be resolved, the error lists close matches.

3. **If the bridge is offline**: `search_nodes`/`get_node_details` use the offline
   catalog (pin types may be "Object" — trust names, not types). build_patch still
   works and falls back to the catalog.

4. **Templates**: `list_templates` / `get_template("path")` before generating
   shaders or C# nodes. `knowledge/templates/vl/basic_vl_objects.vl` shows every
   document building block (definitions, regions, interfaces) as XML.

5. **Deeper reference (only if needed)**:
   - `read_knowledge("vl-building-blocks")` — definitions, regions, pads, channels
   - `read_knowledge("vl-file-format")` — full XML spec
   - `read_knowledge("vl-project-architecture")` — multi-document project scaffolding
   - `read_knowledge("gray-book-language")` — official language reference

6. **Iterate on errors**: if build_patch reports compile errors, fix the spec and
   re-run — or use `remove_node` + a follow-up build_patch with only links to
   rewire. Errors carry elementId matching the XML Id attributes.
""";
    }

    [McpServerPrompt(Name = "explain_vl_patch")]
    [Description("Generate a detailed explanation for a vvvv gamma .vl patch file.")]
    public string ExplainVlPatch(
        [Description("Absolute path to the .vl patch file to explain")] string filePath)
    {
        return
            $"Explain the vvvv gamma patch at: {filePath}\n\n" +
            PatternsInstruction + "\n\n" +
            $"""
## Analysis workflow

1. Call `read_patch("{filePath}")` for the structured graph data.
2. Call `explain_patch("{filePath}")` for a natural-language overview.
3. For unknown nodes: `get_node_details("NodeName")` or `search_nodes("query")`.
4. For deeper context (only if patterns resource doesn't cover it):
   - `read_knowledge("vvvv-shaders")` — SDSL / TextureFX / DrawFX
   - `read_knowledge("vvvv-channels")` — IChannel, reactive patterns
   - `read_knowledge("gray-book-libraries")` — Stride, Skia, CoreLib API reference
   - `search_practical("topic")` — real forum solutions and help-patch examples

## Explanation to produce

- **Purpose** — what does this patch do?
- **Dataflow** — how data moves from inputs through nodes to outputs
- **Key nodes** — what each significant node contributes
- **Patterns used** — spreading, reactive, 3D rendering, channels, regions, etc.
- **Dependencies** — which packages are required and why
""";
    }

    [McpServerPrompt(Name = "create_csharp_node")]
    [Description("Get guidance on creating a custom C# node for vvvv gamma.")]
    public string CreateCsharpNode(
        [Description("Description of what the custom node should do")] string description)
    {
        return
            $"Create a custom C# node for vvvv gamma: \"{description}\"\n\n" +
            "**Step 0**: call `get_template(\"csharp/Process.cs\")` or " +
            "`get_template(\"csharp/Utils.cs\")` to see the real template before writing code. " +
            "Also `get_template(\"csharp/Template.csproj\")` for the correct .csproj.\n\n" +
            "Call `read_knowledge(\"vvvv-custom-nodes\")` for the complete guide. Key summary:\n\n" +
            "## Stateful Process Node (`[ProcessNode]`)\n\n" +
            "```csharp\nnamespace MyLib;\n\n" +
            "[ProcessNode]\npublic class MyNode : IDisposable\n{\n" +
            "    private float _last;\n    private float _cached;\n\n" +
            "    // out params FIRST in Update() — they become output pins\n" +
            "    public void Update(\n" +
            "        out float result,\n" +
            "        float input = 0f,\n" +
            "        bool reset = false)\n" +
            "    {\n" +
            "        if (input != _last || reset)\n        {\n" +
            "            _cached = Compute(input);\n            _last = input;\n        }\n" +
            "        result = _cached;\n    }\n\n" +
            "    public void Dispose() { }\n" +
            "    private float Compute(float v) => v * v;\n}\n```\n\n" +
            "## Stateless Operation Node\n\n" +
            "```csharp\nnamespace MyLib;\n\npublic static class MathNodes\n{\n" +
            "    public static float Remap(\n" +
            "        float value, float inMin = 0f, float inMax = 1f,\n" +
            "        float outMin = 0f, float outMax = 1f)\n    {\n" +
            "        float t = (value - inMin) / (inMax - inMin);\n" +
            "        return outMin + t * (outMax - outMin);\n    }\n}\n```\n\n" +
            "## Rules\n" +
            "- `out` parameters **first** in `Update()` — vvvv reads them as output pins\n" +
            "- Add dirty-check caching for expensive operations\n" +
            "- `IDisposable.Dispose()` for cleanup (file handles, subscriptions, GPU resources)\n" +
            "- Static methods in a `public static class` are automatically operation nodes\n\n" +
            "## Steps\n" +
            "1. Read the C# templates (step 0 above).\n" +
            "2. Decide: needs state → `[ProcessNode]`; pure → static method.\n" +
            "3. Call `create_csharp_plugin` tool to scaffold files, then edit logic.\n" +
            "4. vvvv compiles `.cs` files live on save — no restart needed for source projects.";
    }
}
