using System.Text;
using VvvvMcp.Core.Models;
using Microsoft.Extensions.Logging;

namespace VvvvMcp.Core.Services;

public class PatchExplainerService
{
    private readonly ILogger<PatchExplainerService> _logger;
    private readonly NodeCatalogService _catalog;

    public PatchExplainerService(
        ILogger<PatchExplainerService> logger,
        NodeCatalogService catalog)
    {
        _logger = logger;
        _catalog = catalog;
    }

    public string ExplainPatch(PatchGraph patch, string? filePath = null)
    {
        var sb = new StringBuilder();

        if (filePath is not null)
        {
            sb.AppendLine($"# Patch: {Path.GetFileName(filePath)}");
        }
        else
        {
            sb.AppendLine("# Patch Analysis");
        }
        sb.AppendLine();

        if (patch.LanguageVersion is not null)
        {
            sb.AppendLine($"**Language Version:** {patch.LanguageVersion}");
        }

        if (patch.Dependencies.Any())
        {
            sb.AppendLine();
            sb.AppendLine("## Dependencies");
            foreach (var dep in patch.Dependencies)
            {
                sb.AppendLine($"- **{dep.Location}** (v{dep.Version ?? "unknown"})");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Nodes");
        sb.AppendLine($"Total: {patch.AllNodes.Count} nodes, {patch.AllPads.Count} value pads, {patch.Links.Count} connections");
        sb.AppendLine();

        var categorizedNodes = patch.AllNodes
            .Where(n => n.Reference.NodeName is not null)
            .GroupBy(n => n.Reference.LastCategoryFullName ?? "Uncategorized")
            .OrderBy(g => g.Key);

        foreach (var group in categorizedNodes)
        {
            sb.AppendLine($"### {group.Key}");
            foreach (var node in group)
            {
                var name = node.Reference.NodeName ?? node.Name ?? "Unknown";
                var dep = node.Reference.LastDependency ?? "";
                var visiblePins = node.Pins.Where(p => !p.IsHidden).ToList();
                var inputs = visiblePins.Where(p => p.Kind == "InputPin").ToList();
                var outputs = visiblePins.Where(p => p.Kind is "OutputPin" or "StateOutputPin").ToList();

                sb.AppendLine($"- **{name}** (from {dep})");
                
                if (inputs.Any())
                {
                    sb.AppendLine($"  - Inputs: {string.Join(", ", inputs.Select(p => FormatPin(p)))}");
                }
                if (outputs.Any())
                {
                    sb.AppendLine($"  - Outputs: {string.Join(", ", outputs.Select(p => FormatPin(p)))}");
                }

                if (_catalog.IsLoaded)
                {
                    var catalogNodes = _catalog.GetByName(name);
                    var match = catalogNodes.FirstOrDefault();
                    if (match is not null && !string.IsNullOrEmpty(match.Summary))
                    {
                        sb.AppendLine($"  - *{match.Summary}*");
                    }
                }
            }
            sb.AppendLine();
        }

        var valuePads = patch.AllPads.Where(p => p.Value is not null || p.IsIOBox).ToList();
        if (valuePads.Any())
        {
            sb.AppendLine("## Values / IO Boxes");
            foreach (var pad in valuePads)
            {
                var type = pad.TypeName ?? "Unknown";
                var value = pad.Value ?? "(no default)";
                sb.AppendLine($"- {type}: {value}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Dataflow Connections");
        sb.AppendLine($"Total: {patch.Links.Count} connections");
        sb.AppendLine();

        var pinToOwner = new Dictionary<string, string>();
        foreach (var node in patch.AllNodes)
        {
            var nodeName = node.Reference.NodeName ?? node.Name ?? node.Id;
            foreach (var pin in node.Pins)
            {
                pinToOwner[pin.Id] = $"{nodeName}.{pin.Name}";
            }
        }
        foreach (var pad in patch.AllPads)
        {
            var padDesc = pad.TypeName is not null 
                ? $"[{pad.TypeName}: {pad.Value ?? "?"}]" 
                : $"[Pad {pad.Id[..8]}]";
            pinToOwner[pad.Id] = padDesc;
        }

        foreach (var link in patch.Links)
        {
            var source = pinToOwner.GetValueOrDefault(link.SourceId, link.SourceId);
            var target = pinToOwner.GetValueOrDefault(link.TargetId, link.TargetId);
            sb.AppendLine($"- {source} → {target}");
        }

        return sb.ToString();
    }

    private static string FormatPin(PatchPin pin)
    {
        var parts = new List<string> { pin.Name };
        if (pin.DefaultValue is not null)
        {
            parts.Add($"={pin.DefaultValue}");
        }
        return string.Join("", parts);
    }
}
