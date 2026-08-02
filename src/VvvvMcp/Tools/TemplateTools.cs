using System.ComponentModel;
using ModelContextProtocol.Server;
using VvvvMcp.Core.Services;

namespace VvvvMcp.Tools;

/// <summary>
/// MCP tools for accessing vvvv template files.
///
/// Templates live in knowledge/templates/ and are the ground-truth starting
/// points for generating VL patches, C# nodes, and SDSL shaders.
/// Reading a template before generating gives the AI correct, real-world
/// structure rather than relying solely on reconstructed examples.
/// </summary>
[McpServerToolType]
public class TemplateTools
{
    private readonly TemplateService _templates;

    public TemplateTools(TemplateService templates)
    {
        _templates = templates;
    }

    [McpServerTool(Name = "list_templates")]
    [Description("""
        List all available vvvv template files organized by category.
        
        Categories:
          vl/     – VL patch templates (.vl): empty application, HDE editor extension
          csharp/ – C# node templates (.cs, .csproj): ProcessNode, static operation, dynamic enum
          sdsl/   – SDSL shader templates (.sdsl): TextureFX filter/mixer/source, ComputeFX, DrawFX, ShaderFX
        
        Always call this before create_csharp_plugin or create_shader to see which templates
        are available — they contain correct, real-world patterns.
        Use get_template to fetch the full content of any template.
        """)]
    public object ListTemplates(
        [Description("Optional category filter: 'vl', 'csharp', or 'sdsl'. Omit to list all.")] string? category = null)
    {
        if (!_templates.IsLoaded)
            return new { error = "Templates not loaded. Ensure knowledge/templates/ exists in the knowledge directory." };

        var list = _templates.ListTemplates(category);

        return new
        {
            count = list.Count,
            templates = list.Select(t => new
            {
                path = t.RelativePath,
                category = t.Category,
                name = t.Name,
                extension = t.Extension,
                description = t.Description
            }).ToArray()
        };
    }

    [McpServerTool(Name = "get_template")]
    [Description("""
        Get the full content of a vvvv template file.
        
        Use the relative path from list_templates, e.g.:
          "sdsl/shaders/TextureFX-Filter_TextureFX.sdsl"
          "csharp/Process.cs"
          "csharp/DynamicEnum.cs"
          "csharp/Template.csproj"
          "vl/empty_new_patch.vl"
          "vl/template.HDE.vl"
        
        Templates are the canonical starting point for:
          - SDSL shaders: shows correct base class, attribute syntax, stream variables
          - C# nodes: shows [ProcessNode] pattern, correct Update() signature, namespace setup
          - VL patches: shows exact XML structure with correct element nesting and ID format
        
        Always read the relevant template before generating or editing — especially for shaders,
        where the inheritance hierarchy (FilterBase, MixerBase, ComputeShaderBase, etc.) and
        stream variable names are critical and easy to get wrong.
        """)]
    public object GetTemplate(
        [Description("Relative path to the template file, e.g. 'sdsl/shaders/TextureFX-Filter_TextureFX.sdsl'")] string path)
    {
        if (!_templates.IsLoaded)
            return new { error = "Templates not loaded." };

        var template = _templates.GetTemplate(path);
        if (template is null)
        {
            var available = _templates.ListTemplates().Select(t => t.RelativePath).ToArray();
            return new
            {
                error = $"Template not found: '{path}'",
                available_paths = available
            };
        }

        return new
        {
            path = template.RelativePath,
            category = template.Category,
            name = template.Name,
            extension = template.Extension,
            content = template.Content
        };
    }
}
