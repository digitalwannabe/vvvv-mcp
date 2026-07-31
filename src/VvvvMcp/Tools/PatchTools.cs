using System.ComponentModel;
using ModelContextProtocol.Server;
using VvvvMcp.Core.Services;

namespace VvvvMcp.Tools;

[McpServerToolType]
public class PatchTools
{
    private readonly PatchReaderService _patchReader;
    private readonly PatchExplainerService _explainer;

    public PatchTools(PatchReaderService patchReader, PatchExplainerService explainer)
    {
        _patchReader = patchReader;
        _explainer = explainer;
    }

    [McpServerTool(Name = "read_patch")]
    [Description("Parse a vvvv gamma .vl patch file and return its complete structure including nodes, connections, dependencies, and values.")]
    public object ReadPatch(
        [Description("Absolute path to the .vl file")] string filePath)
    {
        try
        {
            var patch = _patchReader.ReadPatch(filePath);
            return new
            {
                file = Path.GetFileName(filePath),
                documentId = patch.DocumentId,
                languageVersion = patch.LanguageVersion,
                dependencies = patch.Dependencies.Select(d => new
                {
                    d.Location,
                    d.Version
                }),
                nodes = patch.AllNodes.Select(n => new
                {
                    n.Id,
                    name = n.Reference.NodeName ?? n.Name,
                    category = n.Reference.LastCategoryFullName,
                    dependency = n.Reference.LastDependency,
                    kind = n.Reference.Kind,
                    n.Bounds,
                    pins = n.Pins.Where(p => !p.IsHidden).Select(p => new
                    {
                        p.Name,
                        p.Kind,
                        p.DefaultValue
                    })
                }),
                pads = patch.AllPads.Select(p => new
                {
                    p.Id,
                    type = p.TypeName,
                    p.Value,
                    p.IsIOBox
                }),
                connections = patch.Links.Select(l => new
                {
                    l.SourceId,
                    l.TargetId
                }),
                stats = new
                {
                    nodeCount = patch.AllNodes.Count,
                    padCount = patch.AllPads.Count,
                    linkCount = patch.Links.Count,
                    dependencyCount = patch.Dependencies.Count
                }
            };
        }
        catch (FileNotFoundException)
        {
            return new { error = $"File not found: {filePath}" };
        }
        catch (Exception ex)
        {
            return new { error = $"Failed to parse patch: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "explain_patch")]
    [Description("Get a human-readable natural language explanation of a vvvv gamma .vl patch file. Describes what the patch does, its nodes, dataflow, and dependencies.")]
    public object ExplainPatch(
        [Description("Absolute path to the .vl file")] string filePath)
    {
        try
        {
            var patch = _patchReader.ReadPatch(filePath);
            var explanation = _explainer.ExplainPatch(patch, filePath);
            return new { explanation };
        }
        catch (FileNotFoundException)
        {
            return new { error = $"File not found: {filePath}" };
        }
        catch (Exception ex)
        {
            return new { error = $"Failed to explain patch: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "list_patch_dependencies")]
    [Description("List all NuGet package dependencies of a vvvv gamma .vl patch file.")]
    public object ListPatchDependencies(
        [Description("Absolute path to the .vl file")] string filePath)
    {
        try
        {
            var patch = _patchReader.ReadPatch(filePath);
            return new
            {
                file = Path.GetFileName(filePath),
                dependencies = patch.Dependencies.Select(d => new
                {
                    d.Location,
                    d.Version
                }),
                count = patch.Dependencies.Count
            };
        }
        catch (FileNotFoundException)
        {
            return new { error = $"File not found: {filePath}" };
        }
        catch (Exception ex)
        {
            return new { error = $"Failed to read dependencies: {ex.Message}" };
        }
    }
}
