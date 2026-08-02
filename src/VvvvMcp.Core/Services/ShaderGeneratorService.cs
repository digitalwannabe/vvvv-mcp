using Microsoft.Extensions.Logging;

namespace VvvvMcp.Core.Services;

public record GeneratedShader(
    string FilePath,
    string Content
);

/// <summary>
/// Generates SDSL shader files for vvvv gamma.
/// 
/// When a TemplateService is provided and loaded, the generator uses the real
/// SDSL template files from knowledge/templates/sdsl/shaders/ as its base — this
/// ensures the inheritance hierarchy, attribute syntax, and stream variables are
/// correct according to the actual Stride/vvvv shader system.
/// 
/// Fallback (no templates): hardcoded minimal stubs are used.
/// </summary>
public class ShaderGeneratorService
{
    private readonly ILogger<ShaderGeneratorService> _logger;
    private readonly TemplateService? _templateService;

    // Template placeholder names that will be replaced by the user's name
    private static readonly Dictionary<string, string> TemplatePlaceholders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["filter"]    = "TextureFX-Filter",
        ["mixer"]     = "TextureFX-Mixer",
        ["source"]    = "TextureFX-Source",
        ["compute"]   = "ComputeFX",
        ["draw"]      = "DrawFX",
        ["shaderfx"]  = "ShaderFX",
    };

    public ShaderGeneratorService(ILogger<ShaderGeneratorService> logger, TemplateService? templateService = null)
    {
        _logger = logger;
        _templateService = templateService;
    }

    public GeneratedShader GenerateTextureFX(
        string name,
        string outputDirectory,
        string? description = null,
        string variant = "filter")
    {
        variant = variant.ToLowerInvariant() switch
        {
            "mixer" => "mixer",
            "source" => "source",
            _ => "filter"
        };

        var content = TryFromTemplate(name, variant, description)
            ?? FallbackTextureFX(name, variant, description);

        var suffix = variant switch
        {
            "mixer"  => "TextureFX-Mixer",
            "source" => "TextureFX-Source",
            _        => "TextureFX-Filter"
        };
        var filePath = Path.Combine(outputDirectory, "shaders", $"{name}_{suffix.Split('-').Last()}.sdsl");
        // Keep standard naming: Name_TextureFX.sdsl
        filePath = Path.Combine(outputDirectory, "shaders", $"{name}_TextureFX.sdsl");

        _logger.LogInformation("Generated TextureFX shader '{Name}' (variant: {Variant})", name, variant);
        return new GeneratedShader(filePath, content);
    }

    public GeneratedShader GenerateComputeShader(
        string name,
        string outputDirectory,
        string? description = null,
        int threadCountX = 64,
        int threadCountY = 1,
        int threadCountZ = 1)
    {
        var content = TryFromTemplate(name, "compute", description)
            ?? FallbackCompute(name, description, threadCountX, threadCountY, threadCountZ);

        var filePath = Path.Combine(outputDirectory, "shaders", $"{name}_ComputeFX.sdsl");
        _logger.LogInformation("Generated compute shader '{Name}'", name);
        return new GeneratedShader(filePath, content);
    }

    public GeneratedShader GenerateShaderFX(
        string name,
        string outputDirectory,
        string? description = null)
    {
        var content = TryFromTemplate(name, "shaderfx", description)
            ?? FallbackShaderFX(name, description);

        var filePath = Path.Combine(outputDirectory, "shaders", $"{name}_ShaderFX.sdsl");
        _logger.LogInformation("Generated ShaderFX '{Name}'", name);
        return new GeneratedShader(filePath, content);
    }

    public GeneratedShader GenerateDrawFX(
        string name,
        string outputDirectory,
        string? description = null)
    {
        var content = TryFromTemplate(name, "draw", description)
            ?? FallbackDrawFX(name, description);

        var filePath = Path.Combine(outputDirectory, "shaders", $"{name}_DrawFX.sdsl");
        _logger.LogInformation("Generated DrawFX shader '{Name}'", name);
        return new GeneratedShader(filePath, content);
    }

    public void SaveShader(GeneratedShader shader)
    {
        var dir = Path.GetDirectoryName(shader.FilePath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(shader.FilePath, shader.Content);
        _logger.LogInformation("Saved shader to {Path}", shader.FilePath);
    }

    // ── Template-based generation ─────────────────────────────────────────────

    private string? TryFromTemplate(string name, string variant, string? description)
    {
        if (_templateService?.IsLoaded != true) return null;

        var template = _templateService.FindShaderTemplate(variant);
        if (template is null) return null;

        // Find the placeholder: it's the part before "_TextureFX" / "_ComputeFX" / etc.
        // e.g. "TextureFX-Filter_TextureFX" → replace "TextureFX-Filter" with name
        // e.g. "ComputeFX_ComputeFX" → replace "ComputeFX" with name
        // e.g. "DrawFX_DrawFX" → replace "DrawFX" with name
        var placeholder = TemplatePlaceholders.GetValueOrDefault(variant, template.Name.Split('_')[0]);

        var content = template.Content;

        // Replace placeholder name with user's name
        content = content.Replace(placeholder + "_", name + "_", StringComparison.Ordinal);
        content = content.Replace(placeholder, name, StringComparison.Ordinal);

        // Update [Summary] if description provided
        if (description is not null)
        {
            content = System.Text.RegularExpressions.Regex.Replace(
                content,
                @"\[Summary\(""\s*""\)\]",
                $"[Summary(\"{EscapeString(description)}\")]");
        }

        return content;
    }

    // ── Fallback generators (no templates available) ──────────────────────────

    private static string FallbackTextureFX(string name, string variant, string? description)
    {
        var descAttr = description is not null ? $"[Summary(\"{EscapeString(description)}\")]\n" : "[Summary(\"\")]\n";

        return variant switch
        {
            "mixer" =>
                "[Category(\"Mixer\")]\n" +
                descAttr +
                $"shader {name}_TextureFX : MixerBase\n" +
                "{\n" +
                "    float4 Mix(float4 tex0col, float4 tex1col, float fader)\n" +
                "    {\n" +
                "        return lerp(tex0col, tex1col, fader);\n" +
                "    }\n" +
                "};\n",

            "source" =>
                "[TextureSource]\n" +
                "[Category(\"Source\")]\n" +
                descAttr +
                $"shader {name}_TextureFX : TextureFX\n" +
                "{\n" +
                "    [Color]\n" +
                "    float4 Color = float4(1, 1, 1, 1);\n" +
                "\n" +
                "    stage override float4 Shading()\n" +
                "    {\n" +
                "        return Color;\n" +
                "    }\n" +
                "};\n",

            _ =>
                "[Category(\"Filter\")]\n" +
                descAttr +
                $"shader {name}_TextureFX : FilterBase\n" +
                "{\n" +
                "    [Color]\n" +
                "    float4 Color = float4(1, 1, 1, 1);\n" +
                "\n" +
                "    float4 Filter(float4 tex0col)\n" +
                "    {\n" +
                "        return tex0col * Color;\n" +
                "    }\n" +
                "};\n"
        };
    }

    private static string FallbackCompute(string name, string? description, int x, int y, int z)
    {
        var descAttr = description is not null ? $"[Summary(\"{EscapeString(description)}\")]\n" : "[Summary(\"\")]\n";
        return descAttr +
            $"shader {name}_ComputeFX : ComputeShaderBase\n" +
            "{\n" +
            "    float Constant = 1;\n" +
            "    RWBuffer<float> Values;\n" +
            "\n" +
            $"    [numthreads({x}, {y}, {z})]\n" +
            "    override void Compute()\n" +
            "    {\n" +
            "        uint index = streams.DispatchThreadId.x;\n" +
            "        Values[index] *= Constant;\n" +
            "    }\n" +
            "};\n";
    }

    private static string FallbackShaderFX(string name, string? description)
    {
        var descAttr = description is not null ? $"[Summary(\"{EscapeString(description)}\")]\n" : "[Summary(\"\")]\n";
        return descAttr +
            $"shader {name}_ShaderFX : ComputeFloat4\n" +
            "{\n" +
            "    float4 MyColor;\n" +
            "\n" +
            "    override float4 Compute()\n" +
            "    {\n" +
            "        return MyColor;\n" +
            "    }\n" +
            "};\n";
    }

    private static string FallbackDrawFX(string name, string? description)
    {
        var descAttr = description is not null ? $"[Summary(\"{EscapeString(description)}\")]\n" : "[Summary(\"\")]\n";
        return descAttr +
            $"shader {name}_DrawFX : VS_PS_Base, ColorBase, ShaderUtils\n" +
            "{\n" +
            "    [Color]\n" +
            "    stage float4 ColorInput = float4(1, 1, 1, 1);\n" +
            "\n" +
            "    override stage void VSMain()\n" +
            "    {\n" +
            "        streams.ShadingPosition = mul(streams.Position, WorldViewProjection);\n" +
            "    }\n" +
            "\n" +
            "    override stage void PSMain()\n" +
            "    {\n" +
            "        streams.ColorTarget = ColorInput;\n" +
            "    }\n" +
            "};\n";
    }

    private static string EscapeString(string s) => s.Replace("\"", "\\\"");
}
