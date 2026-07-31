using System.ComponentModel;
using ModelContextProtocol.Server;
using VvvvMcp.Core.Services;

namespace VvvvMcp.Tools;

[McpServerToolType]
public class PatchWriteTools
{
    private readonly PatchWriterService _writer;
    private readonly NodeCatalogService _catalog;

    public PatchWriteTools(PatchWriterService writer, NodeCatalogService catalog)
    {
        _writer = writer;
        _catalog = catalog;
    }

    [McpServerTool(Name = "create_patch")]
    [Description("Create a new empty vvvv gamma .vl patch file with standard Application process structure.")]
    public object CreatePatch(
        [Description("Absolute file path for the new .vl file")] string filePath,
        [Description("Optional: main category name (default 'Main')")] string category = "Main",
        [Description("Optional: comma-separated NuGet dependencies (default 'VL.CoreLib'). Example: 'VL.CoreLib,VL.Stride'")] string dependencies = "VL.CoreLib")
    {
        try
        {
            if (File.Exists(filePath))
                return new { error = $"File already exists: {filePath}. Use add_node/connect_pins to modify it." };

            var deps = dependencies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var doc = _writer.CreateDocument(category, dependencies: deps);
            _writer.SaveDocument(doc, filePath);

            return new
            {
                success = true,
                filePath,
                category,
                dependencies = deps,
                message = "Patch created. Use add_node and add_pad to add content, then connect_pins to wire them."
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Failed to create patch: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "add_node")]
    [Description("Add a node to a vvvv gamma .vl patch. Returns the new node ID and pin IDs for wiring.")]
    public object AddNode(
        [Description("Absolute path to the .vl file")] string filePath,
        [Description("Node name (e.g. '+', 'Box', 'TransformSRT', 'LFO')")] string nodeName,
        [Description("Full category name (e.g. 'Math', 'Stride.Models', '3D.Transform')")] string category,
        [Description("Dependency .vl file (e.g. 'VL.CoreLib.vl', 'VL.Stride.vl')")] string dependency,
        [Description("Node kind: 'OperationCallFlag' for operations, 'ProcessAppFlag' for stateful processes")] string nodeKind = "OperationCallFlag",
        [Description("Comma-separated pin definitions: 'Name:Kind'. Example: 'Input:InputPin,Output:OutputPin'")] string? pins = null,
        [Description("Optional position as 'x,y,width,height' (auto-positioned if omitted)")] string? bounds = null)
    {
        try
        {
            var doc = _writer.LoadDocument(filePath);

            List<(string Name, string Kind)>? pinList = null;
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

            var result = _writer.AddNode(doc, nodeName, category, dependency, pinList, nodeKind, bounds);
            _writer.SaveDocument(doc, filePath);

            return new
            {
                success = true,
                nodeId = result.NodeId,
                pinIds = result.PinIds,
                message = $"Node '{nodeName}' added. Use connect_pins with the pin IDs to wire it."
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
