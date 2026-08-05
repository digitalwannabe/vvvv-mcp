using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace VvvvMcp.Core.Services;

public record AddNodeResult(
    string NodeId,
    Dictionary<string, string> PinIds  // pin name -> pin id
);

public record AddPadResult(
    string PadId
);

public class PatchWriterService
{
    private readonly ILogger<PatchWriterService> _logger;
    private readonly TemplateService? _templateService;
    private static readonly XNamespace PropNs = "property";
    
    // Track position for auto-layout
    private int _nextY = 200;
    private const int NodeSpacingY = 60;
    private const int DefaultX = 400;

    // Default version read from the empty_new_patch.vl template when available.
    // Hardcoded fallback matches what the template currently contains.
    private const string DefaultLanguageVersion = "2025.7.1-0156-gdf75a792b5";
    private const string DefaultDocVersion      = "0.128";

    public PatchWriterService(ILogger<PatchWriterService> logger, TemplateService? templateService = null)
    {
        _logger          = logger;
        _templateService = templateService;
    }

    /// <summary>
    /// Creates a new VL document with the standard Application process.
    /// If a TemplateService is available, the language version is read from
    /// the empty_new_patch.vl template to stay in sync with the vvvv version
    /// that produced the template.
    /// </summary>
    public XDocument CreateDocument(
        string category = "Main",
        string? languageVersion = null,
        List<string>? dependencies = null)
    {
        // Resolve language version: template → explicit parameter → hardcoded default
        if (languageVersion is null)
            languageVersion = ReadTemplateLanguageVersion() ?? DefaultLanguageVersion;

        dependencies ??= ["VL.CoreLib"];
        
        var createPatchId = VlIdGenerator.NewId();
        var updatePatchId = VlIdGenerator.NewId();
        
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("Document",
                new XAttribute(XNamespace.Xmlns + "p", "property"),
                new XAttribute(XNamespace.Xmlns + "r", "reflection"),
                new XAttribute("Id", VlIdGenerator.NewId()),
                new XAttribute("LanguageVersion", languageVersion),
                new XAttribute("Version", "0.128"),
                new XElement("Patch",
                    new XAttribute("Id", VlIdGenerator.NewId()),
                    new XElement("Canvas",
                        new XAttribute("Id", VlIdGenerator.NewId()),
                        new XAttribute("DefaultCategory", category),
                        new XAttribute("BordersChecked", "false"),
                        new XAttribute("CanvasType", "FullCategory")
                    ),
                    new XElement("Node",
                        new XAttribute("Name", "Application"),
                        new XAttribute("Bounds", "100,100"),
                        new XAttribute("Id", VlIdGenerator.NewId()),
                        new XElement(PropNs + "NodeReference",
                            new XElement("Choice",
                                new XAttribute("Kind", "ContainerDefinition"),
                                new XAttribute("Name", "Process")
                            ),
                            new XElement("CategoryReference",
                                new XAttribute("Kind", "Category"),
                                new XAttribute("Name", "Primitive")
                            )
                        ),
                        new XElement("Patch",
                            new XAttribute("Id", VlIdGenerator.NewId()),
                            new XElement("Canvas",
                                new XAttribute("Id", VlIdGenerator.NewId()),
                                new XAttribute("CanvasType", "Group")
                            ),
                            new XElement("Patch",
                                new XAttribute("Id", createPatchId),
                                new XAttribute("Name", "Create")
                            ),
                            new XElement("Patch",
                                new XAttribute("Id", updatePatchId),
                                new XAttribute("Name", "Update")
                            ),
                            new XElement("ProcessDefinition",
                                new XAttribute("Id", VlIdGenerator.NewId()),
                                new XElement("Fragment",
                                    new XAttribute("Id", VlIdGenerator.NewId()),
                                    new XAttribute("Patch", createPatchId),
                                    new XAttribute("Enabled", "true")
                                ),
                                new XElement("Fragment",
                                    new XAttribute("Id", VlIdGenerator.NewId()),
                                    new XAttribute("Patch", updatePatchId),
                                    new XAttribute("Enabled", "true")
                                )
                            )
                        )
                    )
                )
            )
        );

        // Add dependencies
        var docElement = doc.Root!;
        foreach (var dep in dependencies)
        {
            docElement.Add(new XElement("NugetDependency",
                new XAttribute("Id", VlIdGenerator.NewId()),
                new XAttribute("Location", dep),
                new XAttribute("Version", languageVersion)
            ));
        }

        _nextY = 200; // Reset layout tracker
        _logger.LogInformation("Created new VL document with {DepCount} dependencies", dependencies.Count);
        return doc;
    }

    /// <summary>
    /// Saves an XDocument to a .vl file.
    /// </summary>
    public void SaveDocument(XDocument doc, string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        doc.Save(filePath);
        _logger.LogInformation("Saved VL document to {Path}", filePath);
    }

    /// <summary>
    /// Loads an existing VL file as an XDocument.
    /// </summary>
    public XDocument LoadDocument(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"VL file not found: {filePath}");
        return XDocument.Load(filePath);
    }

    /// <summary>
    /// Adds a node to the main canvas of a VL document.
    /// </summary>
    public AddNodeResult AddNode(
        XDocument doc,
        string nodeName,
        string categoryFullName,
        string dependency,
        List<(string Name, string Kind)>? pins = null,
        string? nodeKind = null,  // "OperationCallFlag" or "ProcessAppFlag"
        string? bounds = null)
    {
        var canvas = GetMainCanvas(doc);
        var nodeId = VlIdGenerator.NewId();
        
        // Auto-detect node kind if not specified
        nodeKind ??= "OperationCallFlag";
        
        // Auto-layout if no bounds specified  
        bounds ??= $"{DefaultX},{_nextY},100,19";
        _nextY += NodeSpacingY;

        // Build the node reference
        var nodeRef = new XElement(PropNs + "NodeReference",
            new XAttribute("LastCategoryFullName", categoryFullName),
            new XAttribute("LastDependency", dependency),
            new XElement("Choice",
                new XAttribute("Kind", "NodeFlag"),
                new XAttribute("Name", "Node"),
                new XAttribute("Fixed", "true")
            ),
            new XElement("Choice",
                new XAttribute("Kind", nodeKind),
                new XAttribute("Name", nodeName)
            )
        );

        var nodeElement = new XElement("Node",
            new XAttribute("Bounds", bounds),
            new XAttribute("Id", nodeId),
            nodeRef
        );

        // Add pins
        var pinIds = new Dictionary<string, string>();
        if (pins is not null)
        {
            foreach (var (pinName, pinKindRaw) in pins)
            {
                // "InputPin:hidden" → hidden pin (e.g. Node Context on process nodes)
                var hidden = pinKindRaw.EndsWith(":hidden", StringComparison.OrdinalIgnoreCase);
                var pinKind = hidden ? pinKindRaw[..^7] : pinKindRaw;

                var pinId = VlIdGenerator.NewId();
                pinIds[pinName] = pinId;
                var pinEl = new XElement("Pin",
                    new XAttribute("Id", pinId),
                    new XAttribute("Name", pinName),
                    new XAttribute("Kind", pinKind)
                );
                if (hidden)
                    pinEl.Add(new XAttribute("IsHidden", "true"));
                nodeElement.Add(pinEl);
            }
        }

        canvas.Add(nodeElement);
        _logger.LogInformation("Added node '{Name}' ({Category}) with {PinCount} pins", 
            nodeName, categoryFullName, pinIds.Count);

        return new AddNodeResult(nodeId, pinIds);
    }

    /// <summary>
    /// Adds a value pad (IOBox) to the main canvas.
    /// </summary>
    public AddPadResult AddPad(
        XDocument doc,
        string typeName,
        string? value = null,
        string typeCategory = "Primitive",
        string typeDependency = "VL.CoreLib.vl",
        string typeKind = "TypeFlag",
        string? bounds = null)
    {
        var canvas = GetMainCanvas(doc);
        var padId = VlIdGenerator.NewId();
        
        bounds ??= $"{DefaultX},{_nextY},35,15";
        _nextY += 30;

        var padElement = new XElement("Pad",
            new XAttribute("Id", padId),
            new XAttribute("Comment", ""),
            new XAttribute("Bounds", bounds),
            new XAttribute("ShowValueBox", "true"),
            new XAttribute("isIOBox", "true"),
            new XElement(PropNs + "TypeAnnotation",
                new XAttribute("LastCategoryFullName", typeCategory),
                new XAttribute("LastDependency", typeDependency),
                new XElement("Choice",
                    new XAttribute("Kind", typeKind),
                    new XAttribute("Name", typeName)
                )
            )
        );

        if (value is not null)
        {
            padElement.SetAttributeValue("Value", value);
        }

        canvas.Add(padElement);
        _logger.LogInformation("Added pad '{Type}' with value '{Value}'", typeName, value ?? "(none)");

        return new AddPadResult(padId);
    }

    /// <summary>
    /// Adds a link (connection) between two pins or between a pad and a pin.
    /// The sourceId should be an output pin ID or pad ID.
    /// The targetId should be an input pin ID.
    /// </summary>
    public string AddLink(XDocument doc, string sourceId, string targetId)
    {
        var appPatch = GetApplicationPatch(doc);
        var linkId = VlIdGenerator.NewId();

        appPatch.Add(new XElement("Link",
            new XAttribute("Id", linkId),
            new XAttribute("Ids", $"{sourceId},{targetId}")
        ));

        _logger.LogInformation("Added link {Source} -> {Target}", sourceId, targetId);
        return linkId;
    }

    /// <summary>
    /// Removes a node by ID and all links connected to its pins.
    /// </summary>
    public void RemoveNode(XDocument doc, string nodeId)
    {
        var root = doc.Root!;
        var node = root.Descendants("Node")
            .FirstOrDefault(n => n.Attribute("Id")?.Value == nodeId)
            ?? throw new InvalidOperationException($"Node not found: {nodeId}");

        // Collect all pin IDs of this node
        var pinIds = node.Elements("Pin")
            .Select(p => p.Attribute("Id")?.Value)
            .Where(id => id is not null)
            .Cast<string>()
            .ToHashSet();

        // Also include the node ID itself (for pads that link directly)
        pinIds.Add(nodeId);

        // Remove links that reference any of these pins
        var linksToRemove = root.Descendants("Link")
            .Where(link =>
            {
                var ids = link.Attribute("Ids")?.Value?.Split(',') ?? Array.Empty<string>();
                return ids.Any(id => pinIds.Contains(id.Trim()));
            })
            .ToList();

        foreach (var link in linksToRemove)
            link.Remove();

        node.Remove();
        _logger.LogInformation("Removed node {Id} and {LinkCount} associated links", nodeId, linksToRemove.Count);
    }

    /// <summary>
    /// Removes a pad by ID and all links connected to it.
    /// </summary>
    public void RemovePad(XDocument doc, string padId)
    {
        var root = doc.Root!;
        var pad = root.Descendants("Pad")
            .FirstOrDefault(p => p.Attribute("Id")?.Value == padId)
            ?? throw new InvalidOperationException($"Pad not found: {padId}");

        // Remove links referencing this pad
        var linksToRemove = root.Descendants("Link")
            .Where(link =>
            {
                var ids = link.Attribute("Ids")?.Value?.Split(',') ?? Array.Empty<string>();
                return ids.Any(id => id.Trim() == padId);
            })
            .ToList();

        foreach (var link in linksToRemove)
            link.Remove();

        pad.Remove();
        _logger.LogInformation("Removed pad {Id} and {LinkCount} associated links", padId, linksToRemove.Count);
    }

    /// <summary>
    /// Removes a specific link by ID.
    /// </summary>
    public void RemoveLink(XDocument doc, string linkId)
    {
        var root = doc.Root!;
        var link = root.Descendants("Link")
            .FirstOrDefault(l => l.Attribute("Id")?.Value == linkId)
            ?? throw new InvalidOperationException($"Link not found: {linkId}");

        link.Remove();
        _logger.LogInformation("Removed link {Id}", linkId);
    }

    /// <summary>
    /// Sets or updates the value of a pad.
    /// </summary>
    public void SetPadValue(XDocument doc, string padId, string value)
    {
        var root = doc.Root!;
        var pad = root.Descendants("Pad")
            .FirstOrDefault(p => p.Attribute("Id")?.Value == padId)
            ?? throw new InvalidOperationException($"Pad not found: {padId}");

        pad.SetAttributeValue("Value", value);
        _logger.LogInformation("Set pad {Id} value to '{Value}'", padId, value);
    }

    /// <summary>
    /// Sets a pin's default value.
    /// </summary>
    public void SetPinDefault(XDocument doc, string pinId, string value)
    {
        var root = doc.Root!;
        var pin = root.Descendants("Pin")
            .FirstOrDefault(p => p.Attribute("Id")?.Value == pinId)
            ?? throw new InvalidOperationException($"Pin not found: {pinId}");

        pin.SetAttributeValue("DefaultValue", value);
        _logger.LogInformation("Set pin {Id} default to '{Value}'", pinId, value);
    }

    /// <summary>
    /// Adds a NuGet dependency to the document if not already present.
    /// </summary>
    public void AddDependency(XDocument doc, string packageName, string? version = null)
    {
        var root = doc.Root!;
        var existing = root.Elements("NugetDependency")
            .FirstOrDefault(d => d.Attribute("Location")?.Value == packageName);

        if (existing is not null)
        {
            _logger.LogInformation("Dependency '{Package}' already exists, skipping", packageName);
            return;
        }

        version ??= root.Attribute("LanguageVersion")?.Value ?? "2025.7.0";
        root.Add(new XElement("NugetDependency",
            new XAttribute("Id", VlIdGenerator.NewId()),
            new XAttribute("Location", packageName),
            new XAttribute("Version", version)
        ));

        _logger.LogInformation("Added dependency '{Package}' v{Version}", packageName, version);
    }

    // Helper: find the main canvas (inside Application > Patch > Canvas[CanvasType=Group])
    private XElement GetMainCanvas(XDocument doc)
    {
        var root = doc.Root ?? throw new InvalidOperationException("Document has no root");
        
        // Find Application node
        var appNode = root.Descendants("Node")
            .FirstOrDefault(n => n.Attribute("Name")?.Value == "Application")
            ?? throw new InvalidOperationException("No Application node found");

        // Find the inner canvas (CanvasType=Group)
        var canvas = appNode.Descendants("Canvas")
            .FirstOrDefault(c => c.Attribute("CanvasType")?.Value == "Group")
            ?? throw new InvalidOperationException("No Group canvas found inside Application");

        // Update _nextY based on existing nodes
        var existingBounds = canvas.Elements()
            .Select(e => e.Attribute("Bounds")?.Value)
            .Where(b => b is not null)
            .Select(b => {
                var parts = b!.Split(',');
                return parts.Length >= 2 && int.TryParse(parts[1], out var y) ? y : 0;
            });
        
        var maxY = existingBounds.DefaultIfEmpty(150).Max();
        _nextY = Math.Max(_nextY, maxY + NodeSpacingY);

        return canvas;
    }

    // Helper: find the Application's inner Patch element (where links go)
    private XElement GetApplicationPatch(XDocument doc)
    {
        var root = doc.Root ?? throw new InvalidOperationException("Document has no root");
        var appNode = root.Descendants("Node")
            .FirstOrDefault(n => n.Attribute("Name")?.Value == "Application")
            ?? throw new InvalidOperationException("No Application node found");

        // The inner Patch is a direct child of the Application node
        // It contains the Canvas, Create/Update patches, ProcessDefinition, and Links
        var innerPatch = appNode.Elements("Patch").FirstOrDefault()
            ?? throw new InvalidOperationException("No inner Patch found in Application node");

        return innerPatch;
    }

    /// <summary>
    /// Reads the LanguageVersion attribute from the empty_new_patch.vl template
    /// so created documents stay in sync with the vvvv version used to author templates.
    /// Returns null if the template is not loaded.
    /// </summary>
    private string? ReadTemplateLanguageVersion()
    {
        try
        {
            var tmpl = _templateService?.GetEmptyPatchTemplate();
            if (tmpl is null) return null;

            var doc  = XDocument.Parse(tmpl.Content);
            return doc.Root?.Attribute("LanguageVersion")?.Value;
        }
        catch
        {
            return null;
        }
    }
}
