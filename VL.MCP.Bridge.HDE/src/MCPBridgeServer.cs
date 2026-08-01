using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using VL.Core;

namespace VL.MCP.Bridge;

/// <summary>
/// Process node that runs an HTTP bridge server inside vvvv.
/// Place this in a .HDE.vl extension to auto-start with the editor.
/// 
/// The server exposes JSON endpoints that the vvvv-mcp server can call
/// to get live information about the running vvvv instance.
/// </summary>
[ProcessNode]
public class MCPBridgeServer : IDisposable
{
    private WebApplication? _app;
    private Task? _serverTask;
    private CancellationTokenSource? _cts;
    private int _currentPort;
    private readonly BridgeState _state = new();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>
    /// The port the bridge server listens on.
    /// </summary>
    public int Port { get; private set; } = 7123;

    /// <summary>
    /// Whether the server is currently running.
    /// </summary>
    public bool IsRunning => _app is not null && _serverTask is not null && !_serverTask.IsCompleted;

    /// <summary>
    /// Last error message if the server failed to start.
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Update is called every frame by vvvv.
    /// </summary>
    public void Update(
        NodeContext nodeContext,
        int port = 7123,
        bool enabled = true)
    {
        if (!enabled)
        {
            Stop();
            return;
        }

        // Restart if port changed
        if (port != _currentPort && _app is not null)
        {
            Stop();
        }

        if (_app is null && enabled)
        {
            _currentPort = port;
            Port = port;
            Start(nodeContext);
        }

        // Update state from vvvv session each frame
        UpdateState(nodeContext);
    }

    private void Start(NodeContext nodeContext)
    {
        try
        {
            _cts = new CancellationTokenSource();

            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.ConfigureKestrel(opts =>
            {
                opts.Listen(IPAddress.Loopback, _currentPort);
            });

            _app = builder.Build();
            MapEndpoints(_app, nodeContext);

            _serverTask = _app.RunAsync(_cts.Token);
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _app = null;
            _serverTask = null;
        }
    }

    private void Stop()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
        if (_app is not null)
        {
            try { _app.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2)); }
            catch { /* best effort */ }
            _app = null;
        }
        _serverTask = null;
    }

    private void MapEndpoints(WebApplication app, NodeContext nodeContext)
    {
        // Health check / discovery
        app.MapGet("/api/ping", () => Results.Json(new
        {
            status = "ok",
            server = "VL.MCP.Bridge",
            version = "0.1.0",
            timestamp = DateTimeOffset.UtcNow
        }, JsonOpts));

        // List open documents
        app.MapGet("/api/documents", () =>
            Results.Json(_state.Documents, JsonOpts));

        // Get compilation errors and warnings
        app.MapGet("/api/errors", () =>
            Results.Json(_state.Errors, JsonOpts));

        // Get running state (is the patch running, paused, etc.)
        app.MapGet("/api/state", () => Results.Json(new
        {
            isRunning = _state.IsRunning,
            isPaused = _state.IsPaused,
            frameCount = _state.FrameCount,
            uptimeSeconds = _state.UptimeSeconds
        }, JsonOpts));

        // Request vvvv to reload a specific file (trigger hot-reload)
        app.MapPost("/api/reload", async (HttpContext ctx) =>
        {
            var body = await JsonSerializer.DeserializeAsync<ReloadRequest>(
                ctx.Request.Body, JsonOpts);
            if (body?.FilePath is null)
                return Results.BadRequest(new { error = "filePath required" });

            var result = ReloadFile(nodeContext, body.FilePath);
            return Results.Json(result, JsonOpts);
        });

        // Get info about installed/referenced packages
        app.MapGet("/api/packages", () =>
            Results.Json(_state.Packages, JsonOpts));

        // Get public channels (if any are exposed)
        app.MapGet("/api/channels", () =>
            Results.Json(_state.Channels, JsonOpts));
    }

    /// <summary>
    /// Update internal state snapshot from the vvvv session.
    /// Called once per frame.
    /// </summary>
    private void UpdateState(NodeContext nodeContext)
    {
        try
        {
            _state.FrameCount++;
            _state.UptimeSeconds = (float)(DateTime.UtcNow - _state.StartTime).TotalSeconds;

            // Access the vvvv session via NodeContext.AppHost
            var appHost = nodeContext.AppHost;

            // --- Documents ---
            _state.UpdateDocuments(appHost);

            // --- Errors ---
            _state.UpdateErrors(appHost);

            // --- Running state ---
            _state.UpdateRunningState(appHost);
        }
        catch
        {
            // Silently ignore - we don't want bridge state collection to crash the editor
        }
    }

    private static object ReloadFile(NodeContext nodeContext, string filePath)
    {
        try
        {
            // Signal vvvv to re-read the file from disk
            // vvvv normally hot-reloads on file save, but we can force it
            var appHost = nodeContext.AppHost;

            // The simplest approach: touch the file's last-write timestamp
            // which triggers vvvv's file watcher
            if (File.Exists(filePath))
            {
                File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow);
                return new { success = true, filePath };
            }
            return new { success = false, error = "File not found: " + filePath };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}

internal record ReloadRequest(string? FilePath);
