using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace VvvvMcp.Core.Services;

/// <summary>
/// Client that connects to the VL.MCP.HDE running inside a live vvvv instance.
/// Provides access to editor state, compilation errors, and live values.
/// Degrades gracefully when the bridge is not available.
/// </summary>
public class BridgeClientService : IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<BridgeClientService> _logger;
    private string _baseUrl;
    private bool _isAvailable;
    private DateTime _lastCheck = DateTime.MinValue;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public BridgeClientService(ILogger<BridgeClientService> logger)
    {
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        // Default port, can be overridden via env var
        var port = Environment.GetEnvironmentVariable("VVVV_MCP_BRIDGE_PORT") ?? "7123";
        _baseUrl = $"http://127.0.0.1:{port}";
    }

    /// <summary>Point the client at a specific port (e.g. the bridge's own loopback inside vvvv).</summary>
    public void SetPort(int port)
    {
        _baseUrl = $"http://127.0.0.1:{port}";
        _lastCheck = DateTime.MinValue; // force re-probe
    }

    /// <summary>
    /// Whether a vvvv bridge is detected and responding.
    /// Caches the result for a few seconds to avoid hammering.
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            if (DateTime.UtcNow - _lastCheck > CheckInterval)
            {
                _ = CheckAvailabilityAsync();
            }
            return _isAvailable;
        }
    }

    /// <summary>
    /// Explicitly check if the bridge is available.
    /// </summary>
    public async Task<bool> CheckAvailabilityAsync()
    {
        _lastCheck = DateTime.UtcNow;
        try
        {
            var response = await _http.GetAsync($"{_baseUrl}/api/ping");
            _isAvailable = response.IsSuccessStatusCode;
            if (_isAvailable)
            {
                _logger.LogDebug("vvvv bridge detected at {Url}", _baseUrl);
            }
        }
        catch
        {
            _isAvailable = false;
        }
        return _isAvailable;
    }

    /// <summary>
    /// Get the ping/status info from the bridge.
    /// </summary>
    public async Task<BridgePingResponse?> PingAsync()
    {
        return await GetAsync<BridgePingResponse>("/api/ping");
    }

    /// <summary>
    /// List all currently open documents in vvvv.
    /// </summary>
    public async Task<List<BridgeDocumentInfo>?> GetDocumentsAsync()
    {
        return await GetAsync<List<BridgeDocumentInfo>>("/api/documents");
    }

    /// <summary>
    /// Get current compilation errors and warnings.
    /// </summary>
    public async Task<List<BridgeErrorInfo>?> GetErrorsAsync()
    {
        return await GetAsync<List<BridgeErrorInfo>>("/api/errors");
    }

    /// <summary>
    /// Get the running state of vvvv.
    /// </summary>
    public async Task<BridgeStateInfo?> GetStateAsync()
    {
        return await GetAsync<BridgeStateInfo>("/api/state");
    }

    /// <summary>
    /// Request vvvv to reload a file from disk.
    /// </summary>
    public async Task<BridgeReloadResult?> ReloadFileAsync(string filePath)
    {
        return await PostAsync<BridgeReloadResult>("/api/reload",
            new { filePath });
    }

    /// <summary>
    /// Get list of referenced packages.
    /// </summary>
    public async Task<List<BridgePackageInfo>?> GetPackagesAsync()
    {
        return await GetAsync<List<BridgePackageInfo>>("/api/packages");
    }

    /// <summary>
    /// Get public channels exposed in the running patch.
    /// </summary>
    public async Task<List<BridgeChannelInfo>?> GetChannelsAsync()
    {
        return await GetAsync<List<BridgeChannelInfo>>("/api/channels");
    }

    // ── Document Operations ───────────────────────────────────────────────

    /// <summary>
    /// Open a .vl document in vvvv.
    /// </summary>
    public async Task<BridgeOperationResult?> OpenDocumentAsync(string filePath)
    {
        return await PostAsync<BridgeOperationResult>("/api/documents/open",
            new { filePath });
    }

    /// <summary>
    /// Create a new .vl document.
    /// </summary>
    public async Task<BridgeOperationResult?> NewDocumentAsync(string filePath)
    {
        return await PostAsync<BridgeOperationResult>("/api/documents/new",
            new { filePath });
    }

    /// <summary>
    /// Close a document by file path.
    /// </summary>
    public async Task<BridgeOperationResult?> CloseDocumentAsync(string filePath, bool save = false)
    {
        return await PostAsync<BridgeOperationResult>("/api/documents/close",
            new { filePath, save });
    }

    /// <summary>
    /// Save a specific document.
    /// </summary>
    public async Task<BridgeOperationResult?> SaveDocumentAsync(string filePath)
    {
        return await PostAsync<BridgeOperationResult>("/api/documents/save",
            new { filePath });
    }

    /// <summary>
    /// Save all open documents.
    /// </summary>
    public async Task<BridgeOperationResult?> SaveAllAsync()
    {
        return await PostAsync<BridgeOperationResult>("/api/documents/save-all", new { });
    }

    /// <summary>
    /// Navigate to / open a document (brings to front if already open).
    /// </summary>
    public async Task<BridgeOperationResult?> NavigateAsync(string filePath)
    {
        return await PostAsync<BridgeOperationResult>("/api/navigate",
            new { filePath });
    }

    // ── Log ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Get recent log entries from vvvv's console.
    /// </summary>
    public async Task<BridgeLogResponse?> GetLogAsync(int limit = 50, string? severity = null)
    {
        var query = $"/api/log?limit={limit}";
        if (severity is not null) query += $"&severity={severity}";
        return await GetAsync<BridgeLogResponse>(query);
    }

    // ── Tabs / Editor ─────────────────────────────────────────────────────

    /// <summary>
    /// Get list of open tabs/patches in the editor.
    /// </summary>
    public async Task<BridgeTabsResponse?> GetTabsAsync()
    {
        return await GetAsync<BridgeTabsResponse>("/api/tabs");
    }

    /// <summary>
    /// Close a specific tab in the editor.
    /// </summary>
    public async Task<BridgeOperationResult?> CloseTabAsync(string filePath, string? canvasId = null)
    {
        return await PostAsync<BridgeOperationResult>("/api/tabs/close",
            new { filePath, canvasId });
    }

    /// <summary>
    /// Undo last action on the active canvas.
    /// </summary>
    public async Task<BridgeOperationResult?> UndoAsync()
    {
        return await PostAsync<BridgeOperationResult>("/api/undo", new { });
    }

    /// <summary>
    /// Redo last undone action on the active canvas.
    /// </summary>
    public async Task<BridgeOperationResult?> RedoAsync()
    {
        return await PostAsync<BridgeOperationResult>("/api/redo", new { });
    }

    // ── Live node catalog ─────────────────────────────────────────────────

    /// <summary>
    /// Search the LIVE node registry of the running vvvv instance.
    /// Returns null when the bridge or the endpoint (bridge ≥ 0.3) is unavailable.
    /// </summary>
    public async Task<LiveNodeSearchResponse?> SearchLiveNodesAsync(
        string? query, string? category = null, int limit = 30, bool includePins = false)
    {
        var q = $"/api/nodes?limit={limit}&pins={(includePins ? "1" : "0")}";
        if (!string.IsNullOrWhiteSpace(query))    q += $"&query={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrWhiteSpace(category)) q += $"&category={Uri.EscapeDataString(category)}";
        return await GetAsync<LiveNodeSearchResponse>(q);
    }

    /// <summary>
    /// Exact lookup of one node in the live registry, with full pin details.
    /// </summary>
    public async Task<LiveNodeLookupResponse?> LookupLiveNodeAsync(string name, string? category = null)
    {
        var q = $"/api/nodes/lookup?name={Uri.EscapeDataString(name)}";
        if (!string.IsNullOrWhiteSpace(category)) q += $"&category={Uri.EscapeDataString(category)}";
        return await GetAsync<LiveNodeLookupResponse>(q);
    }

    /// <summary>Ask the bridge to rebuild its live node snapshot (e.g. after installing a pack).</summary>
    public async Task<LiveNodeStatsResponse?> RefreshLiveNodesAsync()
    {
        return await GetAsync<LiveNodeStatsResponse>("/api/nodes/stats?refresh=1");
    }

    /// <summary>Live node catalog stats; null when the endpoint doesn't exist (old bridge).</summary>
    public async Task<LiveNodeStatsResponse?> GetLiveNodeStatsAsync()
    {
        return await GetAsync<LiveNodeStatsResponse>("/api/nodes/stats");
    }

    /// <summary>
    /// Set a pin's default value on the LIVE running patch via the editor API
    /// (undo-integrated, no reload). documentId resolved from filePath when only that is given.
    /// </summary>
    public async Task<object?> SetPinValueLiveAsync(
        string elementId, string pinName, string value,
        string? documentId = null, string? filePath = null, string? typeHint = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["elementId"] = elementId,
            ["pinName"] = pinName,
            ["value"] = value
        };
        if (!string.IsNullOrEmpty(documentId)) payload["documentId"] = documentId;
        if (!string.IsNullOrEmpty(filePath)) payload["filePath"] = filePath;
        if (!string.IsNullOrEmpty(typeHint)) payload["typeHint"] = typeHint;
        return await PostAsync<object>("/api/pin/set", payload);
    }

    // ── Internal helpers ──────────────────────────────────────────────────

    private async Task<T?> GetAsync<T>(string path) where T : class
    {
        try
        {
            var response = await _http.GetAsync($"{_baseUrl}{path}");
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<T>(JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Bridge GET {Path} failed", path);
            _isAvailable = false;
            return null;
        }
    }

    private async Task<T?> PostAsync<T>(string path, object body) where T : class
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"{_baseUrl}{path}", body, JsonOpts);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<T>(JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Bridge POST {Path} failed", path);
            _isAvailable = false;
            return null;
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        GC.SuppressFinalize(this);
    }
}

// ── Response DTOs (mirror what the bridge server sends) ──────────────────────

public class BridgePingResponse
{
    public string Status { get; set; } = "";
    public string Server { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
}

public class BridgeDocumentInfo
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public bool IsActive { get; set; }
    public bool IsSaved { get; set; }
    public bool IsChanged { get; set; }
    public bool IsReadOnly { get; set; }
}

public class BridgeErrorInfo
{
    public string Message { get; set; } = "";
    public string? Why { get; set; }
    public string? How { get; set; }
    public string? Severity { get; set; }
    public string? Location { get; set; }
    public string? DocumentId { get; set; }
    public string? ElementId { get; set; }
    public string? Source { get; set; }
}

public class BridgeStateInfo
{
    public bool IsRunning { get; set; }
    public bool IsPaused { get; set; }
    public long FrameCount { get; set; }
    public float UptimeSeconds { get; set; }
}

public class BridgeReloadResult
{
    public bool Success { get; set; }
    public string? FilePath { get; set; }
    public string? Error { get; set; }
}

public class BridgePackageInfo
{
    public string Name { get; set; } = "";
    public string? Version { get; set; }
    public string? Source { get; set; }
}

public class BridgeChannelInfo
{
    public string Name { get; set; } = "";
    public string? Type { get; set; }
    public string? Value { get; set; }
    public string? Direction { get; set; }
}

public class BridgeOperationResult
{
    public bool Success { get; set; }
    public string? FilePath { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public string? Method { get; set; }
    public string? Action { get; set; }
}

public class BridgeLogResponse
{
    public int Count { get; set; }
    public List<BridgeLogEntry> Entries { get; set; } = new();
}

public class BridgeLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Severity { get; set; } = "";
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Exception { get; set; }
}

public class BridgeTabsResponse
{
    public List<BridgeTabInfo> Tabs { get; set; } = new();
    public int Count { get; set; }
    public string? ActiveTab { get; set; }
    public BridgeCanvasInfo? SelectedCanvas { get; set; }
}

public class BridgeTabInfo
{
    public string? Name { get; set; }
    public string? FilePath { get; set; }
    public string? Type { get; set; }
}

public class BridgeCanvasInfo
{
    public string? Name { get; set; }
    public string? Id { get; set; }
}

// ── Live node catalog DTOs ──────────────────────────────────────────────────

public class LivePinInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string DefaultValue { get; set; } = "";
    public bool IsPinGroup { get; set; }
    public bool Hidden { get; set; }
    public bool Optional { get; set; }
    public bool State { get; set; }
}

public class LiveNodeInfo
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Kind { get; set; } = "Operation";   // "Process" | "Operation"
    public string Package { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public List<LivePinInfo> Inputs { get; set; } = new();
    public List<LivePinInfo> Outputs { get; set; } = new();
}

public class LiveNodeSearchResponse
{
    public int Total { get; set; }
    public int Count { get; set; }
    public DateTime BuiltAt { get; set; }
    public List<LiveNodeInfo> Nodes { get; set; } = new();
}

public class LiveNodeLookupResponse
{
    public bool Found { get; set; }
    public int MatchCount { get; set; }
    public List<LiveNodeInfo> Nodes { get; set; } = new();
    public List<string> Suggestions { get; set; } = new();
}

public class LiveNodeStatsResponse
{
    public int Nodes { get; set; }
    public DateTime BuiltAt { get; set; }
    public bool Stale { get; set; }
    public string? LastError { get; set; }
}
