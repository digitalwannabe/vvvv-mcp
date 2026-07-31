using System.ComponentModel;
using ModelContextProtocol.Server;
using VvvvMcp.Core.Services;

namespace VvvvMcp.Tools;

[McpServerToolType]
public class GeneratorTools
{
    private readonly PluginGeneratorService _pluginGen;
    private readonly ShaderGeneratorService _shaderGen;

    public GeneratorTools(PluginGeneratorService pluginGen, ShaderGeneratorService shaderGen)
    {
        _pluginGen = pluginGen;
        _shaderGen = shaderGen;
    }

    [McpServerTool(Name = "create_csharp_plugin")]
    [Description("Generate a C# custom node plugin for vvvv gamma. Creates a .cs and .csproj file.")]
    public object CreateCsharpPlugin(
        [Description("Name of the node (e.g. 'MyFilter')")] string name,
        [Description("C# namespace (e.g. 'MyNodes')")] string namespaceName,
        [Description("Output directory path")] string outputDirectory,
        [Description("Plugin type: 'operation' or 'process'")] string pluginType = "operation",
        [Description("Inputs as 'Name:Type' comma-separated (e.g. 'value:float,enabled:bool')")] string inputs = "",
        [Description("Outputs as 'Name:Type' comma-separated (e.g. 'result:float')")] string outputs = "",
        [Description("Optional description")] string? summary = null,
        [Description("For process: state fields as 'Name:Type' comma-separated")] string? stateFields = null)
    {
        try
        {
            var inputList = ParseTypedParams(inputs);
            var outputList = ParseTypedParams(outputs);

            GeneratedPlugin plugin;
            if (pluginType == "process")
            {
                var stateList = stateFields is not null ? ParseTypedParams(stateFields) : null;
                plugin = _pluginGen.GenerateProcessPlugin(name, namespaceName, outputDirectory, inputList, outputList, stateList, summary);
            }
            else
            {
                plugin = _pluginGen.GenerateOperationPlugin(name, namespaceName, outputDirectory, inputList, outputList, summary);
            }

            _pluginGen.SavePlugin(plugin);

            return new
            {
                success = true,
                pluginType,
                files = new { csproj = plugin.CsprojPath, cs = plugin.CsFilePath },
                csContent = plugin.CsContent,
                message = $"Plugin '{name}' created. Edit the .cs file to implement logic."
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Failed to create plugin: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "create_shader")]
    [Description("Generate a Stride SDSL shader file for vvvv gamma.")]
    public object CreateShader(
        [Description("Shader name (e.g. 'Blur', 'ColorGrade')")] string name,
        [Description("Output directory (shader goes in 'shaders' subdirectory)")] string outputDirectory,
        [Description("Type: 'texturefx', 'compute', or 'shaderfx'")] string shaderType = "texturefx",
        [Description("Optional description")] string? description = null)
    {
        try
        {
            GeneratedShader shader = shaderType.ToLowerInvariant() switch
            {
                "texturefx" or "texture" or "filter" => _shaderGen.GenerateTextureFX(name, outputDirectory, description),
                "compute" or "computefx" => _shaderGen.GenerateComputeShader(name, outputDirectory, description),
                "shaderfx" or "shader" => _shaderGen.GenerateShaderFX(name, outputDirectory, description),
                _ => throw new ArgumentException($"Unknown shader type '{shaderType}'.")
            };

            _shaderGen.SaveShader(shader);

            return new
            {
                success = true,
                shaderType,
                filePath = shader.FilePath,
                content = shader.Content,
                message = $"Shader '{name}' created at {shader.FilePath}."
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Failed to create shader: {ex.Message}" };
        }
    }

    private static List<(string Name, string Type)> ParseTypedParams(string paramStr)
    {
        if (string.IsNullOrWhiteSpace(paramStr))
            return new List<(string, string)>();

        return paramStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p =>
            {
                var parts = p.Split(':', 2);
                return (parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : "float");
            })
            .ToList();
    }
}
