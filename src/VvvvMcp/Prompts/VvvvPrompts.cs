using System.ComponentModel;
using ModelContextProtocol.Server;

namespace VvvvMcp.Prompts;

[McpServerPromptType]
public static class VvvvPrompts
{
    [McpServerPrompt(Name = "explain_vl_patch")]
    [Description("Generate a detailed explanation prompt for a vvvv gamma VL patch file.")]
    public static string ExplainVlPatch(
        [Description("Absolute path to the .vl patch file to explain")] string filePath)
    {
        return $"""
            Please analyze and explain the vvvv gamma VL patch at:
            {filePath}

            ## vvvv gamma Quick Reference
            vvvv gamma is a live visual programming environment for .NET. Programs run continuously
            at ~60 FPS. Data flows left-to-right through linked nodes. Key concepts:
            - **Process nodes** (ProcessAppFlag in XML) = stateful, have Create+Update+Dispose lifecycle
            - **Operation nodes** (OperationCallFlag) = pure functions, stateless
            - **IOBoxes / Pads** = value editors/displays (Float32, Int32, Vector3, etc.)
            - **Links** = connections between pins; source (output) pin first in Ids attribute
            - **Regions** = ForEach (iterate spreads), If (conditional), Repeat (loop)
            - **Stride** = vvvv's 3D engine; SceneWindow → RootScene → Entity → Model+Material+Transform
            - **VL.CoreLib** categories: 3D, 3D.Transform, Math, Collections, Animation, Adaptive, Control, IO

            ## Steps
            1. Call `read_patch` with the file path to get the structured graph data.
            2. Call `explain_patch` to get an initial natural language overview.
            3. For any node names you don't recognize, call `get_node_details` or `search_nodes`.
            4. If you need more vvvv conceptual context, call `read_knowledge` with one of:
               - 'vl-quickref' — XML cheat sheet, NodeReference patterns, critical rules, topic index
               - 'vvvv-patching' — patching patterns, regions, event handling
               - 'vl-file-format' — VL XML structure details
               - 'vvvv-packages' — package/library reference
               - 'gray-book-language' — official language reference (nodes, patches, regions, type system)
               - 'gray-book-libraries' — official library reference (CoreLib, Stride, collections, reactive)
               - 'gray-book-extending' — official extending guide (writing nodes, shaders)
               - 'vvvv-shaders' — SDSL shader authoring
               - 'vvvv-dotnet' — .NET integration
               - 'vvvv-troubleshooting' — diagnosing errors
            5. Synthesize everything into a clear explanation covering:
               - **What the patch does overall** (purpose and use case)
               - **The dataflow** — how data moves from inputs through processing to outputs
               - **Key nodes and their roles** — what each significant node contributes
               - **Dependencies** — which packages/libraries are required and why
               - **Notable patterns or techniques** used (spreading, reactive, 3D rendering, etc.)
            """;
    }

    [McpServerPrompt(Name = "create_vl_patch")]
    [Description("Get guidance on creating a new vvvv gamma VL patch from scratch.")]
    public static string CreateVlPatch(
        [Description("Description of what the patch should do")] string description)
    {
        return
            "The user wants to create a new vvvv gamma VL patch:\n" +
            "\"" + description + "\"\n\n" +
            "## vvvv gamma VL XML Structure\n\n" +
            "VL patches are XML files with the .vl extension. Key rules:\n" +
            "- All IDs are 22-char base62 alphanumeric strings (unique per document)\n" +
            "- Dependencies are children of Document, NOT of Patch\n" +
            "- `isIOBox` uses lowercase 'i'\n" +
            "- Links: `Ids=\"outputPinId,inputPinId\"` — source/output FIRST\n" +
            "- `Version=\"0.128\"` always required\n" +
            "- `xmlns:p=\"property\"` must be on Document\n\n" +
            "```xml\n" +
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<Document xmlns:p=\"property\" xmlns:r=\"reflection\" Id=\"{unique-id}\" LanguageVersion=\"2025.7.0\" Version=\"0.128\">\n" +
            "  <NugetDependency Id=\"{unique-id}\" Location=\"VL.CoreLib\" Version=\"2025.7.0\" />\n" +
            "  <!-- Add more NugetDependency elements here for VL.Stride, VL.Audio, etc. -->\n" +
            "  <Patch Id=\"{unique-id}\">\n" +
            "    <Canvas Id=\"{unique-id}\" DefaultCategory=\"Main\" CanvasType=\"FullCategory\" />\n" +
            "    <Node Name=\"Application\" Bounds=\"100,100\" Id=\"{unique-id}\">\n" +
            "      <p:NodeReference>\n" +
            "        <Choice Kind=\"ContainerDefinition\" Name=\"Process\" />\n" +
            "        <CategoryReference Kind=\"Category\" Name=\"Primitive\" />\n" +
            "      </p:NodeReference>\n" +
            "      <Patch Id=\"{unique-id}\">\n" +
            "        <Canvas Id=\"{unique-id}\" CanvasType=\"Group\">\n" +
            "          <!-- Nodes and Pads go here -->\n" +
            "        </Canvas>\n" +
            "        <Patch Id=\"{create-patch-id}\" Name=\"Create\" />\n" +
            "        <Patch Id=\"{update-patch-id}\" Name=\"Update\" />\n" +
            "        <ProcessDefinition Id=\"{unique-id}\">\n" +
            "          <Fragment Id=\"{unique-id}\" Patch=\"{create-patch-id}\" Enabled=\"true\" />\n" +
            "          <Fragment Id=\"{unique-id}\" Patch=\"{update-patch-id}\" Enabled=\"true\" />\n" +
            "        </ProcessDefinition>\n" +
            "        <!-- Links go here -->\n" +
            "      </Patch>\n" +
            "    </Node>\n" +
            "  </Patch>\n" +
            "</Document>\n" +
            "```\n\n" +
            "## Node XML patterns\n\n" +
            "**Operation node** (OperationCallFlag = stateless, e.g. +, TransformSRT, Vector (Join)):\n" +
            "```xml\n" +
            "<Node Bounds=\"x,y,w,h\" Id=\"{unique-id}\">\n" +
            "  <p:NodeReference LastCategoryFullName=\"Category\" LastDependency=\"Package.vl\">\n" +
            "    <Choice Kind=\"NodeFlag\" Name=\"Node\" Fixed=\"true\" />\n" +
            "    <Choice Kind=\"OperationCallFlag\" Name=\"NodeName\" />\n" +
            "  </p:NodeReference>\n" +
            "  <Pin Id=\"{unique-id}\" Name=\"Input\" Kind=\"InputPin\" />\n" +
            "  <Pin Id=\"{unique-id}\" Name=\"Output\" Kind=\"OutputPin\" />\n" +
            "</Node>\n" +
            "```\n\n" +
            "**Process node** (ProcessAppFlag = stateful, e.g. Box, RootScene, SceneWindow, LFO):\n" +
            "```xml\n" +
            "<Node Bounds=\"x,y,w,h\" Id=\"{unique-id}\">\n" +
            "  <p:NodeReference LastCategoryFullName=\"Category\" LastDependency=\"Package.vl\">\n" +
            "    <Choice Kind=\"NodeFlag\" Name=\"Node\" Fixed=\"true\" />\n" +
            "    <Choice Kind=\"ProcessAppFlag\" Name=\"NodeName\" />\n" +
            "  </p:NodeReference>\n" +
            "  <Pin Id=\"{unique-id}\" Name=\"Input\" Kind=\"InputPin\" />\n" +
            "  <Pin Id=\"{unique-id}\" Name=\"Output\" Kind=\"OutputPin\" IsHidden=\"true\" />\n" +
            "</Node>\n" +
            "```\n\n" +
            "**IOBox (Pad)** for a Float32 value:\n" +
            "```xml\n" +
            "<Pad Id=\"{unique-id}\" Comment=\"label\" Bounds=\"x,y,w,h\" ShowValueBox=\"true\" isIOBox=\"true\" Value=\"1.0\">\n" +
            "  <p:TypeAnnotation LastCategoryFullName=\"Primitive\" LastDependency=\"VL.CoreLib.vl\">\n" +
            "    <Choice Kind=\"TypeFlag\" Name=\"Float32\" />\n" +
            "  </p:TypeAnnotation>\n" +
            "</Pad>\n" +
            "```\n\n" +
            "**Link** connecting nodes:\n" +
            "`<Link Id=\"{unique-id}\" Ids=\"{source-output-pin-id},{target-input-pin-id}\" />`\n\n" +
            "## Steps to create the patch\n" +
            "1. Call `read_knowledge` with 'vl-quickref' for the XML cheat sheet and topic index.\n" +
            "   Also call `read_knowledge` with 'gray-book-language' for the full official language reference.\n" +
            "2. Call `search_nodes` to find relevant nodes (e.g. 'Box', 'TransformSRT', 'LFO', etc.).\n" +
            "3. Call `get_node_details` for key nodes to know their exact pin names and types.\n" +
            "4. If 3D rendering is needed, call `read_knowledge` with 'vvvv-packages' for Stride scene setup.\n" +
            "5. Design the dataflow: what transforms what, in what order.\n" +
            "6. Generate unique 22-char alphanumeric IDs for every element.\n" +
            "7. Layout nodes with Bounds (x,y increasing left-to-right, top-to-bottom; ~60px vertical spacing).\n" +
            "8. Wire nodes together with Link elements referencing pin IDs.\n" +
            "9. For any Stride 3D scene, the typical chain is: IOBox → TransformSRT → Model → RootScene → SceneWindow.";
    }

    [McpServerPrompt(Name = "create_csharp_node")]
    [Description("Get guidance on creating a custom C# node for vvvv gamma.")]
    public static string CreateCsharpNode(
        [Description("Description of what the custom node should do")] string description)
    {
        return
            "The user wants to create a custom C# node for vvvv gamma:\n" +
            "\"" + description + "\"\n\n" +
            "Call `read_knowledge` with 'vvvv-custom-nodes' for the full guide. Key summary:\n\n" +
            "vvvv gamma custom C# nodes are standard .NET 8 classes/methods. Key patterns:\n\n" +
            "1. **Stateful Process Node** (`[ProcessNode]`):\n" +
            "```csharp\n" +
            "using VL.Core;\n\n" +
            "[ProcessNode]\n" +
            "public class MyNode : IDisposable\n" +
            "{\n" +
            "    private float _last;\n" +
            "    private float _cached;\n\n" +
            "    /// <summary>What this node does.</summary>\n" +
            "    public void Update(\n" +
            "        out float result,     // out params FIRST\n" +
            "        float input = 0f,     // inputs with defaults AFTER\n" +
            "        bool reset = false)\n" +
            "    {\n" +
            "        if (input != _last || reset)\n" +
            "        {\n" +
            "            _cached = Compute(input);\n" +
            "            _last = input;\n" +
            "        }\n" +
            "        result = _cached;\n" +
            "    }\n\n" +
            "    public void Dispose() { /* cleanup */ }\n" +
            "}\n" +
            "```\n\n" +
            "2. **Stateless Operation Node** (static method, no attribute needed):\n" +
            "```csharp\n" +
            "public static class MathOps\n" +
            "{\n" +
            "    /// <summary>Remaps a value from one range to another.</summary>\n" +
            "    public static float Remap(float value, float inMin = 0f, float inMax = 1f,\n" +
            "                              float outMin = 0f, float outMax = 1f)\n" +
            "    {\n" +
            "        float t = (value - inMin) / (inMax - inMin);\n" +
            "        return outMin + t * (outMax - outMin);\n" +
            "    }\n" +
            "}\n" +
            "```\n\n" +
            "3. **Assembly visibility** — add to `Initialization.cs`:\n" +
            "```csharp\n" +
            "[assembly: ImportAsIs(Namespace = \"MyNamespace\", Category = \"MyLib\")]\n" +
            "```\n\n" +
            "4. **Project setup** (.csproj):\n" +
            "```xml\n" +
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <TargetFramework>net8.0</TargetFramework>\n" +
            "    <Nullable>enable</Nullable>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <PackageReference Include=\"VL.Core\" Version=\"2025.7.*\" />\n" +
            "    <PackageReference Include=\"VL.Core.Import\" Version=\"2025.7.*\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n" +
            "```\n\n" +
            "Steps:\n" +
            "1. Determine if the node needs state (use `[ProcessNode]`) or is pure (static method).\n" +
            "2. Design inputs/outputs as method parameters and return values.\n" +
            "3. Add change detection for expensive computations.\n" +
            "4. Create the .cs file and .csproj file.\n" +
            "5. Place them in the vvvv project directory or a `src/` subfolder.";
    }
}
