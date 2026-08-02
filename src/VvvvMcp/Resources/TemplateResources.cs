using System.ComponentModel;
using ModelContextProtocol.Server;
using VvvvMcp.Core.Services;

namespace VvvvMcp.Resources;

/// <summary>
/// MCP resources for the vvvv template files.
/// Exposes the most commonly needed templates as named resources so that MCP
/// clients can reference them without knowing their file paths.
///
/// Use list_templates / get_template tools for dynamic access to all templates.
/// </summary>
[McpServerResourceType]
public class TemplateResources
{
    private readonly TemplateService _templates;

    public TemplateResources(TemplateService templates)
    {
        _templates = templates;
    }

    [McpServerResource(Name = "vvvv Template: Empty Patch (.vl)",
        UriTemplate = "vvvv://templates/vl/empty-patch")]
    [Description("""
        The minimal valid vvvv .vl patch XML. Use this as the reference when:
          - Creating a new .vl file from scratch
          - Verifying the correct XML structure (Document, NugetDependency, Patch, Canvas, Application Node, ProcessDefinition)
          - Checking the LanguageVersion / Version attribute values
        The IDs are example values; generate fresh 22-char base62 IDs for new documents.
        """)]
    public string GetEmptyPatch()
    {
        var t = _templates.GetEmptyPatchTemplate();
        return t?.Content ?? "(template not loaded — run build-knowledge.ps1)";
    }

    [McpServerResource(Name = "vvvv Template: HDE Extension (.vl)",
        UriTemplate = "vvvv://templates/vl/hde-extension")]
    [Description("""
        Template for a vvvv editor extension (.HDE.vl). Shows how to use:
          - Command node (Name, Shortcut, Enabled, Visible, OnExecute pins)
          - WindowFactory + SkiaWindow for custom UI panels
          - Required NuGet references (VL.HDE, VL.Lang, VL.Skia)
        File must be named *.HDE.vl to auto-start when loaded in the editor.
        """)]
    public string GetHdeExtension()
    {
        var t = _templates.GetTemplate("vl/template.HDE.vl");
        return t?.Content ?? "(template not loaded)";
    }

    [McpServerResource(Name = "vvvv Template: ProcessNode C# (.cs)",
        UriTemplate = "vvvv://templates/csharp/process")]
    [Description("""
        Minimal C# [ProcessNode] template for vvvv gamma.
        Shows: namespace, class decorated with [ProcessNode] (implicit via Update()),
        private state fields, Update() method signature.
        Update() runs every frame; all parameters become input pins; return value is the output pin.
        """)]
    public string GetProcessCs()
    {
        var t = _templates.FindCsharpTemplate("process");
        return t?.Content ?? "(template not loaded)";
    }

    [McpServerResource(Name = "vvvv Template: Operation Node C# (.cs)",
        UriTemplate = "vvvv://templates/csharp/operation")]
    [Description("""
        Minimal C# stateless operation (function) node template for vvvv gamma.
        Shows: public static class with static methods that become nodes.
        No state, no [ProcessNode] attribute needed — static methods are automatically nodes.
        """)]
    public string GetOperationCs()
    {
        var t = _templates.FindCsharpTemplate("operation");
        return t?.Content ?? "(template not loaded)";
    }

    [McpServerResource(Name = "vvvv Template: Dynamic Enum C# (.cs)",
        UriTemplate = "vvvv://templates/csharp/dynamic-enum")]
    [Description("""
        Dynamic enum pattern for vvvv gamma C# nodes.
        Shows: DynamicEnumBase<T,TDefinition> + DynamicEnumDefinitionBase<T> classes.
        The definition class holds entries (AddEntry/RemoveEntry/ClearEntries) and notifies vvvv
        when the enum list changes so the node browser updates live.
        """)]
    public string GetDynamicEnumCs()
    {
        var t = _templates.FindCsharpTemplate("enum");
        return t?.Content ?? "(template not loaded)";
    }

    [McpServerResource(Name = "vvvv Template: C# Project File (.csproj)",
        UriTemplate = "vvvv://templates/csharp/csproj")]
    [Description("""
        .csproj template for a vvvv gamma C# node library.
        Targets net8.0, enables nullable + implicit usings, references VL.Core,
        and adds global usings for VL.Core, VL.Core.Import, VL.Lib.Collections, Stride.Core.Mathematics.
        """)]
    public string GetCsproj()
    {
        var t = _templates.GetCsprojTemplate();
        return t?.Content ?? "(template not loaded)";
    }

    [McpServerResource(Name = "vvvv Template: TextureFX Filter Shader (.sdsl)",
        UriTemplate = "vvvv://templates/sdsl/texturefx-filter")]
    [Description("""
        SDSL TextureFX filter shader template (FilterBase).
        Per-pixel processing: receives tex0col (input texture sample) and returns modified color.
        Use for: color correction, blur, edge detection, any per-pixel transformation.
        Category "Filter" places the node in the vvvv Stride TextureFX system.
        """)]
    public string GetTextureFxFilter()
    {
        var t = _templates.FindShaderTemplate("filter");
        return t?.Content ?? "(template not loaded)";
    }

    [McpServerResource(Name = "vvvv Template: TextureFX Mixer Shader (.sdsl)",
        UriTemplate = "vvvv://templates/sdsl/texturefx-mixer")]
    [Description("""
        SDSL TextureFX mixer shader template (MixerBase).
        Blends two input textures: receives tex0col, tex1col, and fader.
        Use for: dissolve, blend modes, compositing two texture layers.
        """)]
    public string GetTextureFxMixer()
    {
        var t = _templates.FindShaderTemplate("mixer");
        return t?.Content ?? "(template not loaded)";
    }

    [McpServerResource(Name = "vvvv Template: TextureFX Source Shader (.sdsl)",
        UriTemplate = "vvvv://templates/sdsl/texturefx-source")]
    [Description("""
        SDSL TextureFX source shader template (TextureFX with [TextureSource]).
        Generates pixel color without reading an input texture (procedural source).
        Use for: solid colors, gradients, noise patterns, procedural generation.
        """)]
    public string GetTextureFxSource()
    {
        var t = _templates.FindShaderTemplate("source");
        return t?.Content ?? "(template not loaded)";
    }

    [McpServerResource(Name = "vvvv Template: ComputeFX Shader (.sdsl)",
        UriTemplate = "vvvv://templates/sdsl/computefx")]
    [Description("""
        SDSL compute shader template (ComputeShaderBase).
        GPU compute dispatch: RWBuffer access, DispatchThreadId, numthreads attribute.
        Use for: particle systems, physics simulation, data transformation on GPU.
        """)]
    public string GetComputeFx()
    {
        var t = _templates.FindShaderTemplate("compute");
        return t?.Content ?? "(template not loaded)";
    }

    [McpServerResource(Name = "vvvv Template: DrawFX Shader (.sdsl)",
        UriTemplate = "vvvv://templates/sdsl/drawfx")]
    [Description("""
        SDSL DrawFX vertex + pixel shader template (VS_PS_Base, ColorBase, ShaderUtils).
        Full VS/PS pipeline: VSMain transforms vertices using WorldViewProjection, PSMain writes color.
        Use for: custom mesh rendering, geometry effects, per-vertex transformations.
        """)]
    public string GetDrawFx()
    {
        var t = _templates.FindShaderTemplate("draw");
        return t?.Content ?? "(template not loaded)";
    }

    [McpServerResource(Name = "vvvv Template: ShaderFX Shader (.sdsl)",
        UriTemplate = "vvvv://templates/sdsl/shaderfx")]
    [Description("""
        SDSL ShaderFX compositor shader template (ComputeFloat4).
        Returns a float4 color value used in vvvv's material compositing system.
        Use for: custom material expressions, shader graph nodes, color computations.
        """)]
    public string GetShaderFx()
    {
        var t = _templates.FindShaderTemplate("shaderfx");
        return t?.Content ?? "(template not loaded)";
    }

    [McpServerResource(Name = "vvvv Template: All Templates",
        UriTemplate = "vvvv://templates/all")]
    [Description("Complete knowledge document listing all vvvv templates with their full content, generated from knowledge/templates/.")]
    public string GetAllTemplates()
    {
        // Try to get the pre-generated vl-templates.md knowledge file
        // It's loaded by KnowledgeService, but TemplateService doesn't have it.
        // Fall back to listing all templates.
        var list = _templates.ListTemplates();
        if (list.Count == 0)
            return "(templates not loaded)";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# vvvv Template Files\n");
        foreach (var t in list)
        {
            sb.AppendLine($"## {t.RelativePath}");
            sb.AppendLine($"> {t.Description}\n");
            var tmpl = _templates.GetTemplate(t.RelativePath);
            if (tmpl is not null)
            {
                var lang = t.Extension switch
                {
                    ".vl" or ".csproj" => "xml",
                    ".cs" => "csharp",
                    ".sdsl" or ".hlsl" => "hlsl",
                    _ => ""
                };
                sb.AppendLine($"```{lang}");
                sb.AppendLine(tmpl.Content.TrimEnd());
                sb.AppendLine("```\n");
            }
        }
        return sb.ToString();
    }
}
