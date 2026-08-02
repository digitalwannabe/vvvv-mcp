using Microsoft.Extensions.Logging;

namespace VvvvMcp.Core.Services;

public record GeneratedPlugin(
    string CsprojPath,
    string CsFilePath,
    string CsprojContent,
    string CsContent
);

/// <summary>
/// Generates C# custom node plugin files for vvvv gamma.
/// 
/// When a TemplateService is provided and loaded, the generator uses the real
/// C# template files from knowledge/templates/csharp/ as its base — this ensures
/// correct VL.Core references, implicit usings, nullable settings, and the
/// [ProcessNode] attribute pattern.
/// 
/// Fallback (no templates): hardcoded minimal stubs are used.
/// </summary>
public class PluginGeneratorService
{
    private readonly ILogger<PluginGeneratorService> _logger;
    private readonly TemplateService? _templateService;

    public PluginGeneratorService(ILogger<PluginGeneratorService> logger, TemplateService? templateService = null)
    {
        _logger = logger;
        _templateService = templateService;
    }

    public GeneratedPlugin GenerateOperationPlugin(
        string name,
        string namespaceName,
        string outputDirectory,
        List<(string Name, string Type)> inputs,
        List<(string Name, string Type)> outputs,
        string? summary = null)
    {
        var csContent = BuildOperationCs(name, namespaceName, inputs, outputs, summary);
        var csprojContent = BuildCsproj(namespaceName);

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
        stateFields ??= [];

        var csContent = BuildProcessCs(name, namespaceName, inputs, outputs, stateFields, summary);
        var csprojContent = BuildCsproj(namespaceName);

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

    // ── C# source generation ──────────────────────────────────────────────────

    private string BuildOperationCs(
        string name,
        string namespaceName,
        List<(string Name, string Type)> inputs,
        List<(string Name, string Type)> outputs,
        string? summary)
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
            : "    // For examples: https://thegraybook.vvvv.org/reference/extending/writing-nodes.html#examples\n";

        // Try to adapt the Utils template if available
        if (_templateService?.IsLoaded == true)
        {
            var tmpl = _templateService.FindCsharpTemplate("operation");
            if (tmpl is not null)
            {
                // The Utils template has: namespace HDE; / public static class Utils / public static float DemoNode(float a, float b)
                // Substitute namespace, class name, and method
                var methodBody = outputs.Count == 0
                    ? $"        // TODO: Implement {name}"
                    : outputs.Count == 1
                        ? $"        return default;"
                        : $"        return ({string.Join(", ", outputs.Select(_ => "default"))});";

                var returnStatement = outputs.Count == 1 ? $"        return default({outputType});" : "        // TODO: implement";

                return $"namespace {namespaceName};\n" +
                       "\n" +
                       $"public static class {name}Nodes\n" +
                       "{\n" +
                       summaryXml +
                       $"    public static {outputType} {name}({inputParams})\n" +
                       "    {\n" +
                       $"        // TODO: Implement {name}\n" +
                       "        throw new NotImplementedException();\n" +
                       "    }\n" +
                       "}\n";
            }
        }

        // Fallback
        return FallbackOperationCs(name, namespaceName, inputParams, outputType, summaryXml);
    }

    private string BuildProcessCs(
        string name,
        string namespaceName,
        List<(string Name, string Type)> inputs,
        List<(string Name, string Type)> outputs,
        List<(string Name, string Type)> stateFields,
        string? summary)
    {
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
            : "    // For examples: https://thegraybook.vvvv.org/reference/extending/writing-nodes.html#examples\n";

        // Build [ProcessNode] decorated class
        return $"namespace {namespaceName};\n" +
               "\n" +
               "[ProcessNode]\n" +
               $"public class {name}\n" +
               "{\n" +
               (fields.Length > 0 ? fields + "\n\n" : "") +
               summaryXml +
               $"    public {returnType} Update({inputParams})\n" +
               "    {\n" +
               $"        // TODO: Implement {name}\n" +
               "        throw new NotImplementedException();\n" +
               "    }\n" +
               "}\n";
    }

    private string BuildCsproj(string namespaceName)
    {
        // Try to use the actual csproj template
        if (_templateService?.IsLoaded == true)
        {
            var tmpl = _templateService.GetCsprojTemplate();
            if (tmpl is not null)
            {
                // The template uses namespace "HDE" — we can return it as-is,
                // since the namespace is defined in .cs files, not in the .csproj.
                // Just return the template content unchanged (it's correct for any project).
                return tmpl.Content;
            }
        }

        return FallbackCsproj();
    }

    // ── Fallbacks ─────────────────────────────────────────────────────────────

    private static string FallbackOperationCs(
        string name,
        string namespaceName,
        string inputParams,
        string outputType,
        string summaryXml)
    {
        return $"namespace {namespaceName};\n" +
               "\n" +
               $"public static class {name}Nodes\n" +
               "{\n" +
               summaryXml +
               $"    public static {outputType} {name}({inputParams})\n" +
               "    {\n" +
               $"        // TODO: Implement {name}\n" +
               "        throw new NotImplementedException();\n" +
               "    }\n" +
               "}\n";
    }

    private static string FallbackCsproj()
    {
        return """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <GenerateDocumentationFile>True</GenerateDocumentationFile>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="VL.Core" Version="2025.7.*" />
              </ItemGroup>
              <ItemGroup>
                <Using Include="VL.Core" />
                <Using Include="VL.Core.Import" />
                <Using Include="VL.Lib.Collections" />
                <Using Include="Stride.Core.Mathematics" />
              </ItemGroup>
            </Project>
            """;
    }

    private static string SanitizeParamName(string name)
    {
        var sanitized = name.Replace(" ", "");
        return char.ToLower(sanitized[0]) + sanitized[1..];
    }
}
