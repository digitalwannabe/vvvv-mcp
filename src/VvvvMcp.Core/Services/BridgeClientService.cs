using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace VvvvMcp.Core.Services;

/// <summary>
/// Client that connects to the VL.MCP.Bridge running inside a live vvvv instance.
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
    public string? Severity { get; set; }
    public string? Location { get; set; }
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
