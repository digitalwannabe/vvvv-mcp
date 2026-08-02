using Microsoft.Extensions.Logging;

namespace VvvvMcp.Core.Services;

/// <summary>
/// Loads and serves template files from knowledge/templates/.
/// Templates are organized into three categories:
///   vl/     – .vl patch templates (minimal app, HDE extension, etc.)
///   csharp/ – .cs and .csproj templates (ProcessNode, static operation, dynamic enum, etc.)
///   sdsl/   – SDSL shader templates (TextureFX filter/mixer/source, ComputeFX, DrawFX, ShaderFX)
///
/// These templates are the ground truth used by ShaderGeneratorService and
/// PluginGeneratorService when creating new files.
/// </summary>
public class TemplateService
{
    private readonly ILogger<TemplateService> _logger;
    private readonly Dictionary<string, TemplateFile> _templates = new(StringComparer.OrdinalIgnoreCase);
    private string? _templatesDir;

    private static readonly string[] SupportedExtensions =
        [".vl", ".cs", ".csproj", ".sdsl", ".hlsl", ".config", ".json", ".xml"];

    public TemplateService(ILogger<TemplateService> logger)
    {
        _logger = logger;
    }

    public bool IsLoaded => _templates.Count > 0;

    public async Task LoadAsync(string knowledgeDirectory, CancellationToken ct = default)
    {
        var templatesDir = Path.Combine(knowledgeDirectory, "templates");
        if (!Directory.Exists(templatesDir))
        {
            _logger.LogWarning("Templates directory not found: {Dir}", templatesDir);
            return;
        }

        _templatesDir = templatesDir;
        _templates.Clear();

        foreach (var file in Directory.GetFiles(templatesDir, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (!SupportedExtensions.Contains(ext)) continue;

            ct.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(templatesDir, file).Replace('\\', '/');
            var parts = relativePath.Split('/');
            var category = parts.Length > 1 ? parts[0] : "other";
            var name = Path.GetFileNameWithoutExtension(file);
            var content = await File.ReadAllTextAsync(file, ct);

            _templates[relativePath] = new TemplateFile(relativePath, category, name, ext, content);
        }

        _logger.LogInformation("Loaded {Count} template files from {Dir}", _templates.Count, templatesDir);
    }

    /// <summary>Returns summaries of all loaded templates, optionally filtered by category.</summary>
    public IReadOnlyList<TemplateSummary> ListTemplates(string? category = null)
    {
        var files = _templates.Values.AsEnumerable();
        if (category is not null)
            files = files.Where(f => f.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        return files
            .OrderBy(f => f.Category)
            .ThenBy(f => f.RelativePath)
            .Select(f => new TemplateSummary(f.RelativePath, f.Category, f.Name, f.Extension,
                GetDescription(f)))
            .ToList();
    }

    /// <summary>Returns the full template file by its relative path (e.g. "sdsl/shaders/TextureFX-Filter_TextureFX.sdsl").</summary>
    public TemplateFile? GetTemplate(string relativePath)
    {
        relativePath = relativePath.Replace('\\', '/');
        return _templates.GetValueOrDefault(relativePath);
    }

    /// <summary>Finds the best matching shader template for a given shader type keyword.</summary>
    public TemplateFile? FindShaderTemplate(string shaderTypeHint)
    {
        var hint = shaderTypeHint.ToLowerInvariant();
        var sdslTemplates = _templates.Values
            .Where(t => t.Category == "sdsl" && t.Extension == ".sdsl")
            .ToList();

        return hint switch
        {
            "filter" or "texturefx" or "texture" =>
                sdslTemplates.FirstOrDefault(t => t.Name.StartsWith("TextureFX-Filter", StringComparison.OrdinalIgnoreCase)),
            "mixer" =>
                sdslTemplates.FirstOrDefault(t => t.Name.StartsWith("TextureFX-Mixer", StringComparison.OrdinalIgnoreCase)),
            "source" =>
                sdslTemplates.FirstOrDefault(t => t.Name.StartsWith("TextureFX-Source", StringComparison.OrdinalIgnoreCase)),
            "compute" or "computefx" =>
                sdslTemplates.FirstOrDefault(t => t.Name.StartsWith("ComputeFX", StringComparison.OrdinalIgnoreCase)),
            "draw" or "drawfx" =>
                sdslTemplates.FirstOrDefault(t => t.Name.StartsWith("DrawFX", StringComparison.OrdinalIgnoreCase)),
            "shaderfx" or "shader" =>
                sdslTemplates.FirstOrDefault(t => t.Name.StartsWith("ShaderFX", StringComparison.OrdinalIgnoreCase)),
            _ =>
                sdslTemplates.FirstOrDefault(t => t.Name.StartsWith("TextureFX-Filter", StringComparison.OrdinalIgnoreCase))
        };
    }

    /// <summary>Finds the best matching C# template for a given plugin type keyword.</summary>
    public TemplateFile? FindCsharpTemplate(string pluginTypeHint)
    {
        var hint = pluginTypeHint.ToLowerInvariant();
        var csTemplates = _templates.Values
            .Where(t => t.Category == "csharp" && t.Extension == ".cs")
            .ToList();

        return hint switch
        {
            "process" or "stateful" =>
                csTemplates.FirstOrDefault(t => t.Name.Equals("Process", StringComparison.OrdinalIgnoreCase)),
            "operation" or "stateless" or "static" =>
                csTemplates.FirstOrDefault(t => t.Name.Equals("Utils", StringComparison.OrdinalIgnoreCase)),
            "enum" or "dynamicenum" =>
                csTemplates.FirstOrDefault(t => t.Name.StartsWith("DynamicEnum", StringComparison.OrdinalIgnoreCase)),
            _ =>
                csTemplates.FirstOrDefault(t => t.Name.Equals("Utils", StringComparison.OrdinalIgnoreCase))
        };
    }

    /// <summary>Returns the C# project template (.csproj).</summary>
    public TemplateFile? GetCsprojTemplate() =>
        _templates.Values.FirstOrDefault(t => t.Extension == ".csproj");

    /// <summary>Returns the empty VL patch template.</summary>
    public TemplateFile? GetEmptyPatchTemplate() =>
        _templates.Values.FirstOrDefault(t => t.Extension == ".vl" && t.Name.Contains("empty", StringComparison.OrdinalIgnoreCase));

    public string? GetTemplatesDirectory() => _templatesDir;

    private static string GetDescription(TemplateFile f) => f.Extension switch
    {
        ".vl"     => f.Name.Contains("HDE",    StringComparison.OrdinalIgnoreCase) ? "HDE editor extension patch template" :
                     f.Name.Contains("empty",  StringComparison.OrdinalIgnoreCase) ? "Minimal empty application patch template" :
                     $"VL patch template — {f.Name}",
        ".cs"     => f.Name.Equals("Process",      StringComparison.OrdinalIgnoreCase) ? "Stateful ProcessNode C# template (has Update() + state fields)" :
                     f.Name.Equals("Utils",         StringComparison.OrdinalIgnoreCase) ? "Stateless operation node C# template (static methods)" :
                     f.Name.StartsWith("DynamicEnum", StringComparison.OrdinalIgnoreCase) ? "Dynamic enum C# template (runtime-populated enum)" :
                     f.Name.StartsWith("StaticEnum",  StringComparison.OrdinalIgnoreCase) ? "Static enum C# template" :
                     $"C# node template — {f.Name}",
        ".csproj" => "C# project file for vvvv custom nodes (.NET 8, VL.Core ref, implicit usings)",
        ".sdsl"   => f.Name.StartsWith("TextureFX-Filter", StringComparison.OrdinalIgnoreCase) ? "TextureFX filter shader template (FilterBase, per-pixel processing)" :
                     f.Name.StartsWith("TextureFX-Mixer",  StringComparison.OrdinalIgnoreCase) ? "TextureFX mixer shader template (MixerBase, two-texture blend)" :
                     f.Name.StartsWith("TextureFX-Source", StringComparison.OrdinalIgnoreCase) ? "TextureFX source shader template (generates pixel output, no input tex)" :
                     f.Name.StartsWith("ComputeFX",        StringComparison.OrdinalIgnoreCase) ? "Compute shader template (ComputeShaderBase, GPU compute dispatch)" :
                     f.Name.StartsWith("DrawFX",           StringComparison.OrdinalIgnoreCase) ? "Draw shader template (VS_PS_Base, vertex + pixel shader)" :
                     f.Name.StartsWith("ShaderFX",         StringComparison.OrdinalIgnoreCase) ? "ShaderFX compositor template (ComputeFloat4, material compositing)" :
                     f.Name.Equals("EmptyShader",          StringComparison.OrdinalIgnoreCase) ? "Minimal empty shader (no base class, for custom inheritance)" :
                     $"SDSL shader template — {f.Name}",
        _         => $"Template — {f.Name}{f.Extension}"
    };
}

public record TemplateFile(
    string RelativePath,   // e.g. "sdsl/shaders/TextureFX-Filter_TextureFX.sdsl"
    string Category,       // "vl", "csharp", "sdsl"
    string Name,           // e.g. "TextureFX-Filter_TextureFX" (no ext)
    string Extension,      // e.g. ".sdsl"
    string Content
);

public record TemplateSummary(
    string RelativePath,
    string Category,
    string Name,
    string Extension,
    string Description
);
