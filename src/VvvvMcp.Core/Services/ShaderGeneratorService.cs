using Microsoft.Extensions.Logging;

namespace VvvvMcp.Core.Services;

public record GeneratedShader(
    string FilePath,
    string Content
);

public class ShaderGeneratorService
{
    private readonly ILogger<ShaderGeneratorService> _logger;

    public ShaderGeneratorService(ILogger<ShaderGeneratorService> logger)
    {
        _logger = logger;
    }

    public GeneratedShader GenerateTextureFX(
        string name,
        string outputDirectory,
        string? description = null)
    {
        var descComment = description is not null ? $"// {description}\n" : "";

        var content =
            descComment +
            "[Category(\"Filter\")]\n" +
            "[Summary(\"" + (description ?? name) + "\")]\n" +
            $"shader {name}_TextureFX : FilterBase\n" +
            "{\n" +
            "    float Amount = 1.0f;\n" +
            "\n" +
            "    float4 Filter(float4 tex0col)\n" +
            "    {\n" +
            "        float4 result = tex0col;\n" +
            "        result = lerp(tex0col, result, Amount);\n" +
            "        return result;\n" +
            "    }\n" +
            "};\n";

        var filePath = Path.Combine(outputDirectory, "shaders", $"{name}_TextureFX.sdsl");
        _logger.LogInformation("Generated TextureFX shader '{Name}'", name);
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
        var descComment = description is not null ? $"// {description}\n" : "";

        var content =
            descComment +
            $"shader {name}_ComputeFX : ComputeShaderBase\n" +
            "{\n" +
            "    RWStructuredBuffer<float4> OutputBuffer;\n" +
            "\n" +
            $"    [numthreads({threadCountX}, {threadCountY}, {threadCountZ})]\n" +
            "    override void Compute()\n" +
            "    {\n" +
            "        uint index = streams.DispatchThreadId.x;\n" +
            "        OutputBuffer[index] = float4(0, 0, 0, 1);\n" +
            "    }\n" +
            "};\n";

        var filePath = Path.Combine(outputDirectory, "shaders", $"{name}_ComputeFX.sdsl");
        _logger.LogInformation("Generated compute shader '{Name}'", name);
        return new GeneratedShader(filePath, content);
    }

    public GeneratedShader GenerateShaderFX(
        string name,
        string outputDirectory,
        string? description = null)
    {
        var descComment = description is not null ? $"// {description}\n" : "";

        var content =
            descComment +
            $"shader {name}_ShaderFX : ComputeColor\n" +
            "{\n" +
            "    [Color] float4 ColorInput = float4(1, 1, 1, 1);\n" +
            "\n" +
            "    override float4 Compute()\n" +
            "    {\n" +
            "        return ColorInput;\n" +
            "    }\n" +
            "};\n";

        var filePath = Path.Combine(outputDirectory, "shaders", $"{name}_ShaderFX.sdsl");
        _logger.LogInformation("Generated ShaderFX '{Name}'", name);
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
}
