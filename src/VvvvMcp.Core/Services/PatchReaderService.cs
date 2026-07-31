using System.Xml.Linq;
using VvvvMcp.Core.Models;
using Microsoft.Extensions.Logging;

namespace VvvvMcp.Core.Services;

public class PatchReaderService
{
    private readonly ILogger<PatchReaderService> _logger;
    private static readonly XNamespace PropNs = "property";
    private static readonly XNamespace ReflNs = "reflection";

    public PatchReaderService(ILogger<PatchReaderService> logger)
    {
        _logger = logger;
    }

    public PatchGraph ReadPatch(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"VL file not found: {filePath}");

        _logger.LogInformation("Parsing VL patch: {Path}", filePath);
        var doc = XDocument.Load(filePath);
        var root = doc.Root ?? throw new InvalidOperationException("Empty XML document");

        var documentId = root.Attribute("Id")?.Value ?? "";
        var langVersion = root.Attribute("LanguageVersion")?.Value;
        var docVersion = root.Attribute("Version")?.Value;

        var dependencies = root.Descendants("NugetDependency")
            .Select(d => new PatchDependency(
                Id: d.Attribute("Id")?.Value ?? "",
                Location: d.Attribute("Location")?.Value ?? "",
                Version: d.Attribute("Version")?.Value
            ))
            .ToList();

        var allNodes = new List<PatchNode>();
        var allPads = new List<PatchPad>();
        var allLinks = new List<PatchLink>();
        var canvases = new List<PatchCanvas>();

        foreach (var patchEl in root.Descendants("Patch"))
        {
            foreach (var linkEl in patchEl.Elements("Link"))
            {
                var ids = linkEl.Attribute("Ids")?.Value?.Split(',') ?? Array.Empty<string>();
                if (ids.Length == 2)
                {
                    allLinks.Add(new PatchLink(
                        Id: linkEl.Attribute("Id")?.Value ?? "",
                        SourceId: ids[0].Trim(),
                        TargetId: ids[1].Trim()
                    ));
                }
            }

            foreach (var canvasEl in patchEl.Elements("Canvas"))
            {
                var canvasNodes = new List<PatchNode>();
                var canvasPads = new List<PatchPad>();

                foreach (var nodeEl in canvasEl.Elements("Node"))
                {
                    var node = ParseNode(nodeEl);
                    canvasNodes.Add(node);
                    allNodes.Add(node);
                }

                foreach (var padEl in canvasEl.Elements("Pad"))
                {
                    var pad = ParsePad(padEl);
                    canvasPads.Add(pad);
                    allPads.Add(pad);
                }

                canvases.Add(new PatchCanvas(
                    Id: canvasEl.Attribute("Id")?.Value ?? "",
                    DefaultCategory: canvasEl.Attribute("DefaultCategory")?.Value,
                    CanvasType: canvasEl.Attribute("CanvasType")?.Value,
                    Nodes: canvasNodes,
                    Pads: canvasPads
                ));
            }
        }

        return new PatchGraph(
            DocumentId: documentId,
            LanguageVersion: langVersion,
            DocumentVersion: docVersion,
            Dependencies: dependencies,
            Canvases: canvases,
            Links: allLinks,
            AllNodes: allNodes,
            AllPads: allPads
        );
    }

    private PatchNode ParseNode(XElement nodeEl)
    {
        var refEl = nodeEl.Element(PropNs + "NodeReference");
        
        string? categoryFullName = refEl?.Attribute("LastCategoryFullName")?.Value;
        string? lastDependency = refEl?.Attribute("LastDependency")?.Value;
        string? kind = null;
        string? nodeName = null;

        if (refEl is not null)
        {
            foreach (var choice in refEl.Elements("Choice"))
            {
                var choiceKind = choice.Attribute("Kind")?.Value;
                var choiceName = choice.Attribute("Name")?.Value;
                
                if (choiceKind is "OperationCallFlag" or "ProcessAppFlag" or "ForwardReference")
                {
                    kind = choiceKind;
                    nodeName = choiceName;
                }
            }
            
            var catRef = refEl.Element("CategoryReference");
            if (catRef is not null && categoryFullName is null)
            {
                categoryFullName = catRef.Attribute("Name")?.Value;
            }
        }

        var pins = nodeEl.Elements("Pin")
            .Select(p => new PatchPin(
                Id: p.Attribute("Id")?.Value ?? "",
                Name: p.Attribute("Name")?.Value ?? "",
                Kind: p.Attribute("Kind")?.Value ?? "",
                IsHidden: bool.TryParse(p.Attribute("IsHidden")?.Value, out var hidden) && hidden,
                DefaultValue: p.Attribute("DefaultValue")?.Value
            ))
            .ToList();

        return new PatchNode(
            Id: nodeEl.Attribute("Id")?.Value ?? "",
            Name: nodeEl.Attribute("Name")?.Value,
            Bounds: nodeEl.Attribute("Bounds")?.Value,
            Reference: new PatchNodeReference(
                LastCategoryFullName: categoryFullName,
                LastDependency: lastDependency,
                Kind: kind,
                NodeName: nodeName
            ),
            Pins: pins
        );
    }

    private PatchPad ParsePad(XElement padEl)
    {
        var typeAnnotation = padEl.Element(PropNs + "TypeAnnotation");
        string? typeName = null;
        string? typeCategory = null;

        if (typeAnnotation is not null)
        {
            typeCategory = typeAnnotation.Attribute("LastCategoryFullName")?.Value;
            var choice = typeAnnotation.Elements("Choice").FirstOrDefault();
            typeName = choice?.Attribute("Name")?.Value;
        }

        return new PatchPad(
            Id: padEl.Attribute("Id")?.Value ?? "",
            Comment: padEl.Attribute("Comment")?.Value,
            Bounds: padEl.Attribute("Bounds")?.Value,
            Value: padEl.Attribute("Value")?.Value,
            IsIOBox: bool.TryParse(padEl.Attribute("isIOBox")?.Value, out var isIO) && isIO,
            TypeName: typeName,
            TypeCategory: typeCategory
        );
    }
}
