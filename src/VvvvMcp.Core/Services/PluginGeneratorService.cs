using Microsoft.Extensions.Logging;

namespace VvvvMcp.Core.Services;

public record GeneratedPlugin(
    string CsprojPath,
    string CsFilePath,
    string CsprojContent,
    string CsContent
);

public class PluginGeneratorService
{
    private readonly ILogger<PluginGeneratorService> _logger;

    public PluginGeneratorService(ILogger<PluginGeneratorService> logger)
    {
        _logger = logger;
    }

    public GeneratedPlugin GenerateOperationPlugin(
        string name,
        string namespaceName,
        string outputDirectory,
        List<(string Name, string Type)> inputs,
        List<(string Name, string Type)> outputs,
        string? summary = null)
    {
        var inputParams = string.Join(", ", inputs.Select(i => $"{i.Type} {SanitizeParamName(i.Name)}"));
        var outputType = outputs.Count switch
        {
            0 => "void",
            1 => outputs[0].Type,
            _ => $"({string.Join(", ", outputs.Select(o => $"{o.Type} {o.Name}"))})"
        };

        var summaryXml = summary is not null
            ? $"    /// <summary>{summary}</summary>\n"
            : "";

        var csContent =
            $"namespace {namespaceName};\n" +
            "\n" +
            $"public static class {name}Nodes\n" +
            "{\n" +
            summaryXml +
            $"    public static {outputType} {name}({inputParams})\n" +
            "    {\n" +
            $"        // TODO: Implement {name}\n" +
            "        throw new System.NotImplementedException();\n" +
            "    }\n" +
            "}\n";

        var csprojContent =
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <TargetFramework>net8.0</TargetFramework>\n" +
            "    <Nullable>enable</Nullable>\n" +
            "  </PropertyGroup>\n" +
            "</Project>\n";

        var csprojPath = Path.Combine(outputDirectory, $"{namespaceName}.csproj");
        var csPath = Path.Combine(outputDirectory, $"{name}.cs");

        _logger.LogInformation("Generated operation plugin '{Name}' in {Dir}", name, outputDirectory);
        return new GeneratedPlugin(csprojPath, csPath, csprojContent, csContent);
    }

    public GeneratedPlugin GenerateProcessPlugin(
        string name,
        string namespaceName,
        string outputDirectory,
        List<(string Name, string Type)> inputs,
        List<(string Name, string Type)> outputs,
        List<(string Name, string Type)>? stateFields = null,
        string? summary = null)
    {
        stateFields ??= new List<(string, string)>();

        var fields = string.Join("\n", stateFields.Select(f =>
            $"    private {f.Type} _{char.ToLower(f.Name[0])}{f.Name[1..]};"));

        var inputParams = string.Join(", ", inputs.Select(i => $"{i.Type} {SanitizeParamName(i.Name)}"));

        var returnType = outputs.Count switch
        {
            0 => "void",
            1 => outputs[0].Type,
            _ => $"({string.Join(", ", outputs.Select(o => $"{o.Type} {o.Name}"))})"
        };

        var summaryXml = summary is not null
            ? $"    /// <summary>{summary}</summary>\n"
            : "";

        var csContent =
            $"namespace {namespaceName};\n" +
            "\n" +
            $"public class {name}\n" +
            "{\n" +
            (fields.Length > 0 ? fields + "\n\n" : "") +
            summaryXml +
            $"    public {returnType} Update({inputParams})\n" +
            "    {\n" +
            $"        // TODO: Implement {name}\n" +
            "        throw new System.NotImplementedException();\n" +
            "    }\n" +
            "}\n";

        var csprojContent =
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <TargetFramework>net8.0</TargetFramework>\n" +
            "    <Nullable>enable</Nullable>\n" +
            "  </PropertyGroup>\n" +
            "</Project>\n";

        var csprojPath = Path.Combine(outputDirectory, $"{namespaceName}.csproj");
        var csPath = Path.Combine(outputDirectory, $"{name}.cs");

        _logger.LogInformation("Generated process plugin '{Name}' in {Dir}", name, outputDirectory);
        return new GeneratedPlugin(csprojPath, csPath, csprojContent, csContent);
    }

    public void SavePlugin(GeneratedPlugin plugin)
    {
        var dir = Path.GetDirectoryName(plugin.CsprojPath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(plugin.CsprojPath, plugin.CsprojContent);
        File.WriteAllText(plugin.CsFilePath, plugin.CsContent);
        _logger.LogInformation("Saved plugin files: {Csproj}, {Cs}", plugin.CsprojPath, plugin.CsFilePath);
    }

    private static string SanitizeParamName(string name)
    {
        var sanitized = name.Replace(" ", "");
        return char.ToLower(sanitized[0]) + sanitized[1..];
    }
}
