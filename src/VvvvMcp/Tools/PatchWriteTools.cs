using System.ComponentModel;
using ModelContextProtocol.Server;
using VvvvMcp.Core.Services;

namespace VvvvMcp.Tools;

[McpServerToolType]
public class PatchWriteTools
{
    private readonly PatchWriterService _writer;
    private readonly NodeCatalogService _catalog;
    private readonly NodeResolutionService _resolver;
    private readonly BridgeClientService _bridge;

    public PatchWriteTools(
        PatchWriterService writer,
        NodeCatalogService catalog,
        NodeResolutionService resolver,
        BridgeClientService bridge)
    {
        _writer = writer;
        _catalog = catalog;
        _resolver = resolver;
        _bridge = bridge;
    }

    [McpServerTool(Name = "create_patch")]
    [Description("Create a new empty vvvv gamma .vl patch file with standard Application process structure. " +
        "mode 'file' (default) writes the .vl from scratch (works without vvvv running); " +
        "mode 'editor' additionally opens it in the running vvvv instance via the bridge.")]
    public async Task<object> CreatePatch(
        [Description("Absolute file path for the new .vl file")] string filePath,
        [Description("Optional: main category name (default 'Main')")] string category = "Main",
        [Description("Optional: comma-separated NuGet dependencies (default 'VL.CoreLib'). Example: 'VL.CoreLib,VL.Stride'")] string dependencies = "VL.CoreLib",
        [Description("'file' (default) or 'editor' (also open in running vvvv)")] string mode = "file")
    {
        try
        {
            if (File.Exists(filePath))
                return new { error = $"File already exists: {filePath}. Use build_patch/add_node to modify it." };

            var deps = dependencies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var doc = _writer.CreateDocument(category, dependencies: deps);
            _writer.SaveDocument(doc, filePath);

            var openedInEditor = false;
            if (mode.Equals("editor", StringComparison.OrdinalIgnoreCase) &&
                await _bridge.CheckAvailabilityAsync())
            {
                var res = await _bridge.OpenDocumentAsync(filePath);
                openedInEditor = res?.Success ?? false;
            }

            return new
            {
                success = true,
                filePath,
                category,
                dependencies = deps,
                openedInEditor,
                message = "Patch created. Prefer build_patch to add a whole connected subgraph in one call."
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Failed to create patch: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "add_node")]
    [Description("Add a single node to a vvvv gamma .vl patch. Pins are auto-declared from the live vvvv registry " +
        "(or catalog fallback) when the 'pins' parameter is omitted — no more manual pin lists. " +
        "The node's NuGet dependency is added to the document automatically. " +
        "For more than one node, prefer build_patch (nodes+links+deps+verify in one call).")]
    public async Task<object> AddNode(
        [Description("Absolute path to the .vl file")] string filePath,
        [Description("Node name (e.g. '+', 'Box', 'TransformSRT', 'LFO')")] string nodeName,
        [Description("Full category name (e.g. 'Math', 'Stride.Models', '3D.Transform'). Optional when the name is unambiguous.")] string? category = null,
        [Description("Dependency .vl file — normally auto-detected from the node resolution; override only if wrong.")] string? dependency = null,
        [Description("Node kind: 'OperationCallFlag' or 'ProcessAppFlag' — auto-detected when omitted.")] string? nodeKind = null,
        [Description("Comma-separated pin definitions: 'Name:Kind'. Omit to auto-declare ALL pins from the resolved node description.")] string? pins = null,
        [Description("Optional position as 'x,y,width,height' (auto-positioned if omitted)")] string? bounds = null,
        [Description("After saving, reload in vvvv and report compile errors (default true)")] bool verify = true)
    {
        try
        {
            var doc = _writer.LoadDocument(filePath);

            // ── Resolve node (live registry → catalog) for pins/kind/dependency ──
            var resolution = await _resolver.ResolveAsync(nodeName, category);
            string? resolvedCategory = category;
            string? resolvedDependency = dependency;
            string? resolvedKind = nodeKind;
            List<(string Name, string Kind)>? pinList = null;
            string? resolutionNote = null;

            if (resolution.Found)
            {
                var r = resolution.Node!;
                resolvedCategory ??= r.Category;
                resolvedDependency ??= r.DependencyFile;
                resolvedKind ??= r.XmlNodeKind;

                if (pins is null)
                {
                    // Auto-declare all pins so the node is fully wired-up-able.
                    // Process nodes carry the hidden infrastructure pin "Node Context";
                    // hidden-by-default pins get IsHidden like a hand-placed node.
                    pinList = new List<(string, string)>();
                    if (resolvedKind == "ProcessAppFlag")
                        pinList.Add(("Node Context", "InputPin:hidden"));
                    pinList.AddRange(r.Inputs.Select(p => (p.Name, p.IsHidden ? "InputPin:hidden" : "InputPin")));
                    pinList.AddRange(r.Outputs.Select(p => (p.Name, p.IsHidden ? "OutputPin:hidden" : "OutputPin")));
                }

                // Auto-add the NuGet dependency
                if (!string.IsNullOrEmpty(r.Package))
                    _writer.AddDependency(doc, r.Package);
            }
            else
            {
                resolutionNote = $"Node '{nodeName}' could not be resolved — adding without pins. " +
                                 (resolution.Suggestions.Count > 0
                                     ? $"Did you mean: {string.Join(", ", resolution.Suggestions)}?"
                                     : "Check the name/category.");
            }

            if (resolvedCategory is null || resolvedDependency is null)
            {
                return new
                {
                    error = $"Cannot determine category/dependency for '{nodeName}'. Provide them explicitly or check the node name.",
                    suggestions = resolution.Suggestions
                };
            }

            // Explicit pin list overrides auto-declaration
            if (pins is not null)
            {
                pinList = pins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(p =>
                    {
                        var parts = p.Split(':', 2);
                        return (parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : "InputPin");
                    })
                    .ToList();
            }

            var result = _writer.AddNode(doc, nodeName, resolvedCategory, resolvedDependency, pinList, resolvedKind, bounds);
            _writer.SaveDocument(doc, filePath);

            // ── Verify: reload + compile errors ──────────────────────────────
            object? verification = null;
            if (verify && await _bridge.CheckAvailabilityAsync())
            {
                try { File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow); } catch { }
                await _bridge.ReloadFileAsync(filePath);
                await Task.Delay(700);
                var errors = await _bridge.GetErrorsAsync();
                if (errors is not null)
                {
                    var errs = errors
                        .Where(e => e.Severity?.Contains("Error", StringComparison.OrdinalIgnoreCase) ?? true)
                        .Take(3)
                        .Select(e => new { e.Message, e.Location })
                        .ToList();
                    verification = new { compileErrors = errs.Count, errors = errs };
                }
            }

            return new
            {
                success = true,
                nodeId = result.NodeId,
                pinIds = result.PinIds,
                resolved = resolution.Found
                    ? new { resolution.Node!.FullName, kind = resolvedKind, package = resolution.Node.Package, origin = resolution.Node.Origin }
                    : null,
                resolutionNote,
                verification,
                message = $"Node '{nodeName}' added."
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Failed to add node: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "add_pad")]
    [Description("Add a value pad (IOBox) to a vvvv gamma .vl patch. Returns the pad ID for wiring.")]
    public object AddPad(
        [Description("Absolute path to the .vl file")] string filePath,
        [Description("Type name (e.g. 'Float32', 'Integer32', 'String', 'Boolean')")] string typeName,
        [Description("Optional initial value (e.g. '0.5', '42', 'hello')")] string? value = null,
        [Description("Optional position as 'x,y,width,height'")] string? bounds = null)
    {
        try
        {
            var doc = _writer.LoadDocument(filePath);
            var result = _writer.AddPad(doc, typeName, value, bounds: bounds);
            _writer.SaveDocument(doc, filePath);

            return new
            {
                success = true,
                padId = result.PadId,
                typeName,
                value,
                message = $"Pad '{typeName}' added. Use connect_pins with padId as source to wire it."
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Failed to add pad: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "connect_pins")]
    [Description("Connect an output pin (or pad) to an input pin in a vvvv .vl patch.")]
    public object ConnectPins(
        [Description("Absolute path to the .vl file")] string filePath,
        [Description("Source ID: output pin ID or pad ID")] string sourceId,
        [Description("Target ID: input pin ID")] string targetId)
    {
        try
        {
            var doc = _writer.LoadDocument(filePath);

            // Validate: reject node IDs passed as pin IDs. Node IDs appear as the
            // Id attribute on <Node> elements; pin IDs appear on <Pin> children.
            // Passing a node ID silently produces an unresolvable connection in vvvv.
            var nodeIds = doc.Descendants("Node")
                .Select(n => n.Attribute("Id")?.Value)
                .Where(id => id is not null)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (nodeIds.Contains(sourceId))
                return new { success = false, error = $"sourceId '{sourceId}' is a NODE id, not a pin id. Use read_patch to get the actual PIN id (the 22-char Id on a <Pin> element under the node)." };
            if (nodeIds.Contains(targetId))
                return new { success = false, error = $"targetId '{targetId}' is a NODE id, not a pin id. Use read_patch to get the actual PIN id (the 22-char Id on a <Pin> element under the node)." };

            var linkId = _writer.AddLink(doc, sourceId, targetId);
            _writer.SaveDocument(doc, filePath);
            return new { success = true, linkId, sourceId, targetId };
        }
        catch (Exception ex)
        {
            return new { error = $"Failed to connect: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "remove_node")]
    [Description("Remove a node from a vvvv .vl patch. Also removes all connected links.")]
    public object RemoveNode(
        [Description("Absolute path to the .vl file")] string filePath,
        [Description("ID of the node to remove")] string nodeId)
    {
        try
        {
            var doc = _writer.LoadDocument(filePath);
            _writer.RemoveNode(doc, nodeId);
            _writer.SaveDocument(doc, filePath);
            return new { success = true, message = $"Node {nodeId} removed." };
        }
        catch (Exception ex)
        {
            return new { error = $"Failed to remove node: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "remove_link")]
    [Description("Remove a connection from a vvvv .vl patch.")]
    public object RemoveLink(
        [Description("Absolute path to the .vl file")] string filePath,
        [Description("ID of the link to remove")] string linkId)
    {
        try
        {
            var doc = _writer.LoadDocument(filePath);
            _writer.RemoveLink(doc, linkId);
            _writer.SaveDocument(doc, filePath);
            return new { success = true, message = $"Link {linkId} removed." };
        }
        catch (Exception ex)
        {
            return new { error = $"Failed to remove link: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "set_value")]
    [Description("Set the value of a pad or pin default in a vvvv .vl patch.")]
    public object SetValue(
        [Description("Absolute path to the .vl file")] string filePath,
        [Description("ID of the pad or pin")] string elementId,
        [Description("The value to set")] string value,
        [Description("Element type: 'pad' or 'pin' (default 'pad')")] string elementType = "pad")
    {
        try
        {
            var doc = _writer.LoadDocument(filePath);
            if (elementType == "pin")
                _writer.SetPinDefault(doc, elementId, value);
            else
                _writer.SetPadValue(doc, elementId, value);
            _writer.SaveDocument(doc, filePath);
            return new { success = true, message = $"Value set to '{value}'." };
        }
        catch (Exception ex)
        {
            return new { error = $"Failed to set value: {ex.Message}" };
        }
    }
}
