using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using VvvvMcp.Core.Services;

namespace VvvvMcp.Tools;

/// <summary>
/// MCP tools that communicate with a live vvvv instance via the VL.MCP.HDE.
/// These tools require the bridge to be running inside vvvv (VL.MCP.HDE.vl loaded).
/// They degrade gracefully when the bridge is not available.
/// </summary>
[McpServerToolType]
public class BridgeTools
{
    private readonly BridgeClientService _bridge;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public BridgeTools(BridgeClientService bridge)
    {
        _bridge = bridge;
    }

    /// <summary>
    /// Check if a live vvvv instance is connected via the MCP bridge.
    /// Returns connection status, vvvv version info, and uptime.
    /// </summary>
    [McpServerTool(Name = "check_bridge_connection"), Description(
        "Check if a live vvvv gamma instance is connected. " +
        "Returns bridge status and vvvv runtime info. " +
        "Use this first to verify if live tools (documents, errors, reload) are available.")]
    public async Task<string> CheckBridgeConnection()
    {
        var available = await _bridge.CheckAvailabilityAsync();
        if (!available)
        {
            return JsonSerializer.Serialize(new
            {
                connected = false,
                message = "No vvvv instance detected. Make sure vvvv is running with VL.MCP.HDE.vl loaded. " +
                          "The bridge listens on localhost:7123 by default (configurable via VVVV_MCP_BRIDGE_PORT env var)."
            }, JsonOpts);
        }

        var ping = await _bridge.PingAsync();
        var state = await _bridge.GetStateAsync();

        return JsonSerializer.Serialize(new
        {
            connected = true,
            bridge = ping,
            runtime = state
        }, JsonOpts);
    }

    /// <summary>
    /// List all documents currently open in the running vvvv instance.
    /// </summary>
    [McpServerTool(Name = "get_running_documents"), Description(
        "List all .vl documents currently open in the running vvvv instance. " +
        "Shows file paths and which document is active. " +
        "Requires the VL.MCP.HDE to be running in vvvv.")]
    public async Task<string> GetRunningDocuments()
    {
        if (!await _bridge.CheckAvailabilityAsync())
            return NoBridgeMessage();

        var docs = await _bridge.GetDocumentsAsync();
        if (docs is null || docs.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                documents = Array.Empty<object>(),
                message = "No documents reported (bridge may still be initializing)"
            }, JsonOpts);
        }

        return JsonSerializer.Serialize(new { documents = docs }, JsonOpts);
    }

    /// <summary>
    /// Get current compilation errors and warnings from the running vvvv instance.
    /// </summary>
    [McpServerTool(Name = "get_vvvv_errors"), Description(
        "Get current compilation errors and warnings from the running vvvv instance. " +
        "Errors carry documentId + elementId (matching the .vl XML Id attributes) so they map to exact nodes. " +
        "Pass filePath to only see errors of one document. " +
        "Use after editing a .vl file to verify. Requires the VL.MCP.HDE to be running in vvvv.")]
    public async Task<string> GetVvvvErrors(
        [Description("Optional: only return errors belonging to this .vl file")] string? filePath = null)
    {
        if (!await _bridge.CheckAvailabilityAsync())
            return NoBridgeMessage();

        var errors = await _bridge.GetErrorsAsync();
        if (errors is null)
            return JsonSerializer.Serialize(new { error = "Failed to get errors from vvvv." }, JsonOpts);

        IEnumerable<BridgeErrorInfo> filtered = errors;

        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            try
            {
                var docId = System.Xml.Linq.XDocument.Load(filePath).Root?.Attribute("Id")?.Value;
                if (!string.IsNullOrEmpty(docId))
                    filtered = filtered.Where(e =>
                        e.DocumentId is null ||
                        e.DocumentId.Equals(docId, StringComparison.OrdinalIgnoreCase));
            }
            catch { }
        }

        var list = filtered.Take(50).ToList();
        if (list.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                errors = Array.Empty<object>(),
                message = "No compilation errors — patch is clean!"
            }, JsonOpts);
        }

        var errorCount = list.Count(e =>
            e.Severity?.Contains("Error", StringComparison.OrdinalIgnoreCase) ?? true);
        var warningCount = list.Count(e =>
            e.Severity?.Contains("Warning", StringComparison.OrdinalIgnoreCase) ?? false);

        return JsonSerializer.Serialize(new
        {
            summary = $"{errorCount} error(s), {warningCount} warning(s)",
            totalInSession = errors.Count,
            errors = list.Select(e => new
            {
                e.Message,
                e.Why,
                e.How,
                e.Severity,
                e.Source,
                e.DocumentId,
                e.ElementId
            })
        }, JsonOpts);
    }

    /// <summary>
    /// Request the running vvvv instance to reload a file from disk.
    /// </summary>
    [McpServerTool(Name = "reload_file_in_vvvv"), Description(
        "Tell the running vvvv instance to reload a specific .vl file from disk. " +
        "Use after editing a .vl file externally to make vvvv pick up the changes. " +
        "Bridge ≥ 0.3 reloads the in-memory document via Document.ReloadAsync (updates the editor UI immediately); " +
        "older bridges only touch the file timestamp, which vvvv does NOT reliably pick up. " +
        "Requires the VL.MCP.HDE to be running in vvvv.")]
    public async Task<string> ReloadFileInVvvv(
        [Description("Absolute path to the .vl file to reload")] string filePath)
    {
        if (!await _bridge.CheckAvailabilityAsync())
            return NoBridgeMessage();

        var result = await _bridge.ReloadFileAsync(filePath);
        if (result is null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Bridge did not respond to reload request"
            }, JsonOpts);
        }

        return JsonSerializer.Serialize(result, JsonOpts);
    }

    /// <summary>
    /// Get the runtime state of the vvvv instance (running, paused, frame count).
    /// </summary>
    [McpServerTool(Name = "get_vvvv_state"), Description(
        "Get the runtime state of the running vvvv instance — " +
        "whether the patch is running or paused, frame count, and uptime. " +
        "Requires the VL.MCP.HDE to be running in vvvv.")]
    public async Task<string> GetVvvvState()
    {
        if (!await _bridge.CheckAvailabilityAsync())
            return NoBridgeMessage();

        var state = await _bridge.GetStateAsync();
        if (state is null)
        {
            return JsonSerializer.Serialize(new
            {
                error = "Could not retrieve state from bridge"
            }, JsonOpts);
        }

        return JsonSerializer.Serialize(new
        {
            isRunning = state.IsRunning,
            isPaused = state.IsPaused,
            frameCount = state.FrameCount,
            uptimeSeconds = state.UptimeSeconds,
            uptime = TimeSpan.FromSeconds(state.UptimeSeconds).ToString(@"hh\:mm\:ss")
        }, JsonOpts);
    }

    // ── Document Operations ───────────────────────────────────────────────────

    [McpServerTool(Name = "open_document_in_vvvv"), Description(
        "Open a .vl document in the running vvvv instance. " +
        "Opens it as a visible tab in the editor. " +
        "Requires the VL.MCP.HDE to be running in vvvv.")]
    public async Task<string> OpenDocumentInVvvv(
        [Description("Absolute path to the .vl file to open")] string filePath)
    {
        if (!await _bridge.CheckAvailabilityAsync())
            return NoBridgeMessage();

        var result = await _bridge.OpenDocumentAsync(filePath);
        return JsonSerializer.Serialize(result, JsonOpts);
    }

    [McpServerTool(Name = "close_document_in_vvvv"), Description(
        "Close a document in the running vvvv instance. " +
        "Optionally saves before closing. " +
        "Requires the VL.MCP.HDE to be running in vvvv.")]
    public async Task<string> CloseDocumentInVvvv(
        [Description("Absolute path to the .vl file to close")] string filePath,
        [Description("Whether to save before closing")] bool save = false)
    {
        if (!await _bridge.CheckAvailabilityAsync())
            return NoBridgeMessage();

        var result = await _bridge.CloseDocumentAsync(filePath, save);
        return JsonSerializer.Serialize(result, JsonOpts);
    }

    [McpServerTool(Name = "save_document_in_vvvv"), Description(
        "Save a specific document in the running vvvv instance. " +
        "Use 'all' as filePath to save all open documents. " +
        "Requires the VL.MCP.HDE to be running in vvvv.")]
    public async Task<string> SaveDocumentInVvvv(
        [Description("Absolute path to the .vl file to save, or 'all' to save everything")] string filePath)
    {
        if (!await _bridge.CheckAvailabilityAsync())
            return NoBridgeMessage();

        var result = filePath.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? await _bridge.SaveAllAsync()
            : await _bridge.SaveDocumentAsync(filePath);

        return JsonSerializer.Serialize(result, JsonOpts);
    }

    // ── Editor / Tabs ─────────────────────────────────────────────────────────

    [McpServerTool(Name = "get_open_tabs"), Description(
        "Get list of open tabs/patches in the vvvv editor. " +
        "Shows which canvases are currently open and which one is active. " +
        "Requires the VL.MCP.HDE to be running in vvvv.")]
    public async Task<string> GetOpenTabs()
    {
        if (!await _bridge.CheckAvailabilityAsync())
            return NoBridgeMessage();

        var result = await _bridge.GetTabsAsync();
        return JsonSerializer.Serialize(result, JsonOpts);
    }

    [McpServerTool(Name = "undo_in_vvvv"), Description(
        "Undo the last action on the active canvas in vvvv. " +
        "Requires the VL.MCP.HDE to be running in vvvv.")]
    public async Task<string> UndoInVvvv()
    {
        if (!await _bridge.CheckAvailabilityAsync())
            return NoBridgeMessage();

        var result = await _bridge.UndoAsync();
        return JsonSerializer.Serialize(result, JsonOpts);
    }

    [McpServerTool(Name = "redo_in_vvvv"), Description(
        "Redo the last undone action on the active canvas in vvvv. " +
        "Requires the VL.MCP.HDE to be running in vvvv.")]
    public async Task<string> RedoInVvvv()
    {
        if (!await _bridge.CheckAvailabilityAsync())
            return NoBridgeMessage();

        var result = await _bridge.RedoAsync();
        return JsonSerializer.Serialize(result, JsonOpts);
    }

    // ── Log / Console ─────────────────────────────────────────────────────────

    [McpServerTool(Name = "get_vvvv_log"), Description(
        "Get recent log entries from the vvvv console. " +
        "Captures Information, Warning, and Error level messages. " +
        "Use severity filter to narrow results. " +
        "Requires the VL.MCP.HDE to be running in vvvv.")]
    public async Task<string> GetVvvvLog(
        [Description("Maximum number of entries to return (default 50)")] int limit = 50,
        [Description("Minimum severity: 'info', 'warning', or 'error'")] string? severity = null)
    {
        if (!await _bridge.CheckAvailabilityAsync())
            return NoBridgeMessage();

        var result = await _bridge.GetLogAsync(limit, severity);
        return JsonSerializer.Serialize(result, JsonOpts);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static string NoBridgeMessage()
    {
        return JsonSerializer.Serialize(new
        {
            connected = false,
            error = "No vvvv bridge detected. These live tools require:\n" +
                    "1. vvvv gamma is running\n" +
                    "2. VL.MCP.HDE.vl is loaded (reference the VL.MCP.HDE package)\n" +
                    "3. Bridge server is enabled (default: localhost:7123)\n\n" +
                    "The other vvvv-mcp tools (patch reading, node search, etc.) work without the bridge."
        }, JsonOpts);
    }
}
