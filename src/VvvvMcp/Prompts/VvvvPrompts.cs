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
        "Read the resource `vvvv://knowledge/patterns`. It contains working XML for every " +
        "common pattern: document skeleton, If/ForEach regions with ControlPoint splicers, " +
        "all IOBox types, Channel/SetValue/Consume, Stride scene, Skia Renderer, and SDSL " +
        "shaders. If this resource is already visible earlier in the conversation, do not " +
        "read it again — use what is already in your context.";

    // ── Prompts ───────────────────────────────────────────────────────────────

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

1. **Use the patterns resource** (step 0) — it has the correct document skeleton,
   node reference XML, region structures, and layout rules. Copy the relevant
   skeleton and substitute fresh 22-char alphanumeric IDs. Do NOT invent XML
   structure from memory.

2. **Find node details**: call `search_nodes("NodeName")` to confirm the exact
   category, dependency, and pin names. Then `get_node_details("NodeName")`.

3. **Templates**: call `list_templates` to see available .vl/.cs/.sdsl templates,
   then `get_template("path")` before generating a shader or C# node.

4. **Deeper reference (only if the patterns resource doesn't cover it)**:
   - `read_knowledge("vl-file-format")` — full XML spec, all element attributes
   - `read_knowledge("vvvv-patching")` — region best practices, event handling
   - `read_knowledge("vvvv-shaders")` — SDSL authoring details
   - `read_knowledge("gray-book-language")` — official language reference
   - `read_knowledge("vvvv-packages")` — package list with NugetDependency locations

5. **Link direction**: `<Link Ids="outputPinId,inputPinId" />` — output/source FIRST.

6. **Validate before saving**:
   - All IDs unique and 22-char alphanumeric
   - `xmlns:p="property"` on `<Document>`, `Version="0.128"` present
   - `NugetDependency` as direct child of `<Document>`, not inside `<Patch>`
   - Inner canvases: `CanvasType="Group"` (never `FullCategory`)
   - Links inside the inner `<Patch>`, not inside `<Canvas>`

7. **Save and verify**: use `create_patch` / `add_node` / `connect_pins` tools
   OR write the XML directly and call `get_vvvv_errors` if the bridge is running.
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
