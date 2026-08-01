using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VL.Core;
using VL.Core.Import;

namespace VL.MCP.Bridge;

/// <summary>
/// Process node that runs an HTTP bridge server inside vvvv.
/// Uses System.Net.HttpListener (no ASP.NET Core dependency).
/// </summary>
[ProcessNode]
public class MCPBridgeServer
{
    private HttpListener? _listener;
    private Task? _serverTask;
    private CancellationTokenSource? _cts;
    private int _currentPort;
    private NodeContext? _nodeContext;
    private readonly BridgeState _state = new();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>
    /// Update is called every frame by vvvv.
    /// </summary>
    public (int Port, bool IsRunning, string? LastError) Update(
        NodeContext nodeContext,
        int port = 7123,
        bool enabled = true)
    {
        _nodeContext = nodeContext;
        string? lastError = null;

        if (!enabled)
        {
            Stop();
            return (port, false, null);
        }

        // Restart if port changed
        if (port != _currentPort && _listener is not null)
        {
            Stop();
        }

        if (_listener is null && enabled)
        {
            _currentPort = port;
            lastError = Start();
        }

        // Update state from vvvv session each frame
        try
        {
            _state.FrameCount++;
            _state.UptimeSeconds = (float)(DateTime.UtcNow - _state.StartTime).TotalSeconds;
            var appHost = nodeContext.AppHost;
            _state.UpdateDocuments(appHost);
            _state.UpdateErrors(appHost);
            _state.UpdateRunningState(appHost);
            _state.UpdatePackages();
        }
        catch { }

        var isRunning = _listener is not null && _listener.IsListening;
        return (port, isRunning, lastError);
    }

    private string? Start()
    {
        try
        {
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{_currentPort}/");
            _listener.Start();

            _serverTask = Task.Run(() => RequestLoop(_cts.Token), _cts.Token);
            return null;
        }
        catch (Exception ex)
        {
            _listener?.Close();
            _listener = null;
            _serverTask = null;
            return ex.Message;
        }
    }

    private async Task RequestLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(ct);
                _ = Task.Run(() => HandleRequest(context), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }
            catch { }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            var path = request.Url?.AbsolutePath ?? "/";
            var method = request.HttpMethod;

            object? result = (method, path) switch
            {
                ("GET", "/api/ping") => new
                {
                    status = "ok",
                    server = "VL.MCP.Bridge",
                    version = "0.1.0",
                    timestamp = DateTimeOffset.UtcNow
                },
                ("GET", "/api/documents") => _state.Documents,
                ("GET", "/api/errors") => _state.Errors,
                ("GET", "/api/state") => new
                {
                    isRunning = _state.IsRunning,
                    isPaused = _state.IsPaused,
                    frameCount = _state.FrameCount,
                    uptimeSeconds = _state.UptimeSeconds
                },
                ("POST", "/api/reload") => HandleReload(request),
                ("GET", "/api/packages") => _state.Packages,
                ("GET", "/api/channels") => _state.Channels,
                ("GET", "/api/debug") => HandleDebug(),
                ("GET", "/api/debug/explore") => HandleExplore(request),
                _ => null
            };

            if (result is null)
            {
                response.StatusCode = 404;
                WriteJson(response, new { error = "Not found", path });
            }
            else
            {
                response.StatusCode = 200;
                WriteJson(response, result);
            }
        }
        catch (Exception ex)
        {
            try
            {
                response.StatusCode = 500;
                WriteJson(response, new { error = ex.Message });
            }
            catch { }
        }
        finally
        {
            try { response.Close(); } catch { }
        }
    }

    /// <summary>
    /// Debug endpoint: reflect over AppHost and VL.Lang to discover the API shape.
    /// </summary>
    private object HandleDebug()
    {
        var info = new Dictionary<string, object?>();

        try
        {
            var appHost = _nodeContext?.AppHost;
            if (appHost is null)
            {
                info["error"] = "NodeContext or AppHost is null";
                return info;
            }

            // AppHost type info
            info["appHostType"] = appHost.GetType().FullName;

            // Check for Global/Parent/Editor AppHost
            var globalProp = typeof(AppHost).GetProperty("Global", BindingFlags.Public | BindingFlags.Static);
            var globalHost = globalProp?.GetValue(null);
            info["globalAppHost"] = globalHost?.GetType().FullName;
            info["globalAppHostIsThis"] = ReferenceEquals(globalHost, appHost);

            // Check RuntimeInstance for parent/session access
            var runtimeType = appHost.GetType();
            var allProps = runtimeType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            info["runtimeInstanceAllProps"] = allProps
                .Select(p => $"{p.Name} : {p.PropertyType.Name} [{(p.CanRead ? "get" : "")}{(p.CanWrite ? " set" : "")}]")
                .ToArray();

            // Try to find Session on the global AppHost
            var targetHost = globalHost ?? appHost;
            var targetServices = targetHost?.GetType().GetProperty("Services")?.GetValue(targetHost);

            // Find VL.Lang assembly
            var vlLangAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "VL.Lang");
            info["vlLangLoaded"] = vlLangAsm is not null;

            if (vlLangAsm is not null)
            {
                var sessionType = vlLangAsm.GetType("VL.Model.VLSession");
                info["sessionTypeFound"] = sessionType?.FullName;

                // Try getting session from global host services
                if (sessionType is not null && targetServices is not null)
                {
                    var getServiceMethod = targetServices.GetType().GetMethod("GetService",
                        new[] { typeof(Type) });
                    var session = getServiceMethod?.Invoke(targetServices, new object[] { sessionType });
                    info["sessionFromGlobal"] = session is not null;

                    if (session is not null)
                    {
                        info["sessionActualType"] = session.GetType().FullName;
                        info["sessionProperties"] = session.GetType()
                            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Select(p => $"{p.Name} : {p.PropertyType.Name}")
                            .Take(40)
                            .ToArray();
                    }
                }

                // Also try: static VLSession.Instance
                if (sessionType is not null)
                {
                    var instanceProp = sessionType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    var sessionInstance = instanceProp?.GetValue(null);
                    info["sessionInstance"] = sessionInstance is not null;

                    if (sessionInstance is not null)
                    {
                        info["sessionInstanceType"] = sessionInstance.GetType().FullName;
                        info["sessionInstanceProperties"] = sessionInstance.GetType()
                            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Select(p => $"{p.Name} : {p.PropertyType.Name}")
                            .Take(40)
                            .ToArray();

                        // Try CurrentSolution
                        var solProp = sessionInstance.GetType().GetProperty("CurrentSolution",
                            BindingFlags.Public | BindingFlags.Instance);
                        info["hasCurrSolution"] = solProp is not null;

                        if (solProp is not null)
                        {
                            var solution = solProp.GetValue(sessionInstance);
                            info["solutionType"] = solution?.GetType().FullName;
                            if (solution is not null)
                            {
                                info["solutionProperties"] = solution.GetType()
                                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                    .Select(p => $"{p.Name} : {p.PropertyType.Name}")
                                    .Take(40)
                                    .ToArray();
                            }
                        }
                    }
                }

                // Check RuntimeInstance-specific properties
                var sessionPropOnRuntime = runtimeType.GetProperty("Session",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (sessionPropOnRuntime is not null)
                {
                    var sess = sessionPropOnRuntime.GetValue(appHost);
                    info["sessionFromRuntime"] = sess is not null;
                    info["sessionFromRuntimeType"] = sess?.GetType().FullName;
                }

                // Try Platform property on RuntimeInstance
                var platformProp = runtimeType.GetProperty("Platform",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (platformProp is not null)
                {
                    var platform = platformProp.GetValue(appHost);
                    info["platformType"] = platform?.GetType().FullName;
                    if (platform is not null)
                    {
                        info["platformProperties"] = platform.GetType()
                            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Select(p => $"{p.Name} : {p.PropertyType.Name}")
                            .Take(20)
                            .ToArray();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            info["exception"] = ex.ToString();
        }

        return info;
    }

    private object HandleReload(HttpListenerRequest request)
    {
        try
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = reader.ReadToEnd();
            var parsed = JsonSerializer.Deserialize<ReloadRequest>(body, JsonOpts);

            if (parsed?.FilePath is null)
                return new { success = false, error = "filePath required" };

            if (File.Exists(parsed.FilePath))
            {
                File.SetLastWriteTimeUtc(parsed.FilePath, DateTime.UtcNow);
                return new { success = true, filePath = parsed.FilePath };
            }
            return new { success = false, error = "File not found: " + parsed.FilePath };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }

    /// <summary>
    /// Generic explorer: navigate VLSession properties by dot-path.
    /// Usage: /api/debug/explore?path=LatestCompilation.Messages
    /// </summary>
    private object HandleExplore(HttpListenerRequest request)
    {
        var info = new Dictionary<string, object?>();
        try
        {
            var queryPath = request.QueryString["path"] ?? "";
            info["path"] = queryPath;

            // Start from VLSession.Instance
            var vlLangAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "VL.Lang");
            if (vlLangAsm is null) return new { error = "VL.Lang not loaded" };

            var sessionType = vlLangAsm.GetType("VL.Model.VLSession");
            if (sessionType is null) return new { error = "VLSession type not found" };

            var instanceProp = sessionType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var current = instanceProp?.GetValue(null);
            if (current is null) return new { error = "VLSession.Instance is null" };

            info["rootType"] = current.GetType().FullName;

            // Navigate the dot-path
            if (!string.IsNullOrEmpty(queryPath))
            {
                var parts = queryPath.Split('.');
                foreach (var part in parts)
                {
                    if (current is null) break;

                    var prop = current.GetType().GetProperty(part,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (prop is null)
                    {
                        info["error"] = $"Property '{part}' not found on {current.GetType().Name}";
                        info["availableProperties"] = current.GetType()
                            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Select(p => $"{p.Name} : {p.PropertyType.Name}")
                            .Take(50)
                            .ToArray();
                        return info;
                    }

                    current = prop.GetValue(current);
                    info["resolvedType"] = current?.GetType().FullName;
                }
            }

            if (current is null)
            {
                info["value"] = null;
                return info;
            }

            // Dump properties of the resolved object
            info["type"] = current.GetType().FullName;
            info["properties"] = current.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => $"{p.Name} : {p.PropertyType.FullName}")
                .Take(60)
                .ToArray();

            // If it's enumerable, try to list items
            if (current is System.Collections.IEnumerable enumerable && current is not string)
            {
                var items = new List<object?>();
                int count = 0;
                foreach (var item in enumerable)
                {
                    if (count >= 10) { items.Add("...(truncated)"); break; }
                    // Get basic info about each item
                    var itemType = item?.GetType();
                    var nameP = itemType?.GetProperty("Name")?.GetValue(item)?.ToString();
                    var pathP = itemType?.GetProperty("FilePath")?.GetValue(item)?.ToString()
                             ?? itemType?.GetProperty("Path")?.GetValue(item)?.ToString();
                    var msgP = itemType?.GetProperty("Message")?.GetValue(item)?.ToString()
                             ?? itemType?.GetProperty("Text")?.GetValue(item)?.ToString();

                    if (nameP is not null || pathP is not null || msgP is not null)
                    {
                        items.Add(new { type = itemType?.Name, name = nameP, path = pathP, message = msgP });
                    }
                    else
                    {
                        items.Add(new { type = itemType?.Name, value = item?.ToString() });
                    }
                    count++;
                }
                info["items"] = items;
                info["itemCount"] = count;

                // Get type of first item for schema discovery
                foreach (var item in enumerable)
                {
                    if (item is not null)
                    {
                        info["itemType"] = item.GetType().FullName;
                        info["itemProperties"] = item.GetType()
                            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Select(p => $"{p.Name} : {p.PropertyType.Name}")
                            .Take(40)
                            .ToArray();
                    }
                    break;
                }
            }
            else
            {
                // Try to get a string representation
                info["valueStr"] = current.ToString();
            }
        }
        catch (Exception ex)
        {
            info["exception"] = ex.Message;
        }
        return info;
    }

    private static void WriteJson(HttpListenerResponse response, object data)
    {
        response.ContentType = "application/json";
        response.AddHeader("Access-Control-Allow-Origin", "*");
        var json = JsonSerializer.SerializeToUtf8Bytes(data, JsonOpts);
        response.ContentLength64 = json.Length;
        response.OutputStream.Write(json, 0, json.Length);
    }

    private void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
        _cts?.Dispose();
        _cts = null;
        _serverTask = null;
    }
}

internal record ReloadRequest(string? FilePath);
