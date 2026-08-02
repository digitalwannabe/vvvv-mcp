using System.Collections;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VL.Core;
using VL.Core.Import;

namespace VL.MCP.Bridge;

/// <summary>
/// Process node that runs an HTTP bridge server inside vvvv.
/// Exposes document management, error monitoring, log capture, and editor navigation.
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
    private readonly BridgeLogCapture _logCapture = new();
    private bool _logProviderRegistered;
    
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

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

        if (port != _currentPort && _listener is not null)
            Stop();

        if (_listener is null && enabled)
        {
            _currentPort = port;
            lastError = Start();
        }

        // Register log capture provider (once)
        if (!_logProviderRegistered)
        {
            try
            {
                var appHost = nodeContext.AppHost;
                appHost.LoggerFactory.AddProvider(_logCapture);
                _logProviderRegistered = true;
            }
            catch { }
        }

        // Update state
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
                // ── Status ──
                ("GET", "/api/ping") => new
                {
                    status = "ok",
                    server = "VL.MCP.Bridge",
                    version = "0.2.0",
                    timestamp = DateTimeOffset.UtcNow
                },
                ("GET", "/api/state") => new
                {
                    isRunning = _state.IsRunning,
                    isPaused = _state.IsPaused,
                    frameCount = _state.FrameCount,
                    uptimeSeconds = _state.UptimeSeconds
                },

                // ── Documents ──
                ("GET", "/api/documents") => _state.Documents,
                ("GET", "/api/errors") => _state.Errors,
                ("GET", "/api/packages") => _state.Packages,

                // ── Document Operations ──
                ("POST", "/api/documents/open") => HandleOpenDocument(request),
                ("POST", "/api/documents/new") => HandleNewDocument(request),
                ("POST", "/api/documents/close") => HandleCloseDocument(request),
                ("POST", "/api/documents/save") => HandleSaveDocument(request),
                ("POST", "/api/documents/save-all") => HandleSaveAll(),
                ("POST", "/api/reload") => HandleReload(request),

                // ── Log/Console ──
                ("GET", "/api/log") => HandleGetLog(request),
                ("DELETE", "/api/log") => HandleClearLog(),

                // ── Navigation ──
                ("POST", "/api/navigate") => HandleNavigate(request),

                // ── Debug ──
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
            try { response.StatusCode = 500; WriteJson(response, new { error = ex.Message }); }
            catch { }
        }
        finally
        {
            try { response.Close(); } catch { }
        }
    }

    // ── Document Operations ──────────────────────────────────────────────────────

    private object HandleOpenDocument(HttpListenerRequest request)
    {
        var body = ReadBody(request);
        var filePath = JsonDocument.Parse(body).RootElement.GetProperty("filePath").GetString();
        if (string.IsNullOrEmpty(filePath))
            return new { success = false, error = "filePath required" };

        if (!File.Exists(filePath))
            return new { success = false, error = $"File not found: {filePath}" };

        try
        {
            var session = GetSession();
            if (session is null)
                return new { success = false, error = "Session not available" };

            var addRootMethod = session.GetType().GetMethod("AddAsRootDocument",
                BindingFlags.Public | BindingFlags.Instance);

            // Check if document is already in solution
            var solution = session.GetType().GetProperty("CurrentSolution")?.GetValue(session);
            var docsEnum = solution?.GetType().GetProperty("Documents")?.GetValue(solution) as IEnumerable;
            object? existingDoc = null;
            if (docsEnum is not null)
            {
                foreach (var d in docsEnum)
                {
                    var fp2 = d.GetType().GetProperty("FilePath")?.GetValue(d)?.ToString();
                    if (string.Equals(fp2, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        existingDoc = d;
                        break;
                    }
                }
            }

            if (existingDoc is not null && addRootMethod is not null)
            {
                // Document already in solution - try shell execute to trigger vvvv's 
                // file association handler (single-instance forwards to running instance)
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath!)
                    {
                        UseShellExecute = true
                    });
                    return new { success = true, filePath, method = "ShellExecute (already in solution)" };
                }
                catch
                {
                    // Fallback to AddAsRootDocument
                    PostToUIThread(() => addRootMethod.Invoke(session, new[] { existingDoc }));
                    return new { success = true, filePath, method = "AddAsRootDocument (fallback)" };
                }
            }

            // Document not in solution - shell execute it (vvvv will open it)
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath!)
                {
                    UseShellExecute = true
                });
                return new { success = true, filePath, method = "ShellExecute" };
            }
            catch (Exception ex2)
            {
                return new { success = false, error = $"ShellExecute failed: {ex2.Message}" };
            }
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }

    private object HandleNewDocument(HttpListenerRequest request)
    {
        var body = ReadBody(request);
        var filePath = JsonDocument.Parse(body).RootElement.GetProperty("filePath").GetString();
        if (string.IsNullOrEmpty(filePath))
            return new { success = false, error = "filePath required" };

        try
        {
            var session = GetSession();
            if (session is null)
                return new { success = false, error = "Session not available" };

            // GenerateNewDocumentPath ensures unique filename
            var genMethod = session.GetType().GetMethod("GenerateNewDocumentPath",
                BindingFlags.Public | BindingFlags.Instance);
            var actualPath = genMethod?.Invoke(session, new object[] { filePath })?.ToString() ?? filePath;

            // NewDocumentAsync(path, token, progress)
            var method = session.GetType().GetMethod("NewDocumentAsync",
                new[] { typeof(string), typeof(CancellationToken), typeof(IProgress<>) .MakeGenericType(
                    AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }})
                        .FirstOrDefault(t => t.Name == "LoadMessage") ?? typeof(object)
                )});

            // Fallback: try simpler approach via LoadDocumentInBackground on a new file
            if (method is null)
            {
                // Create a minimal .vl file first
                var minimalVl = """
                    <?xml version="1.0" encoding="utf-8"?>
                    <Document xmlns:p="property" xmlns:r="reflection" Id="NewDoc000000000000000" LanguageVersion="2025.7.1" Version="0.128">
                      <NugetDependency Id="NewDocDep00000000000000" Location="VL.CoreLib" Version="2025.7.1" />
                      <Patch Id="NewDocPatch000000000000" />
                    </Document>
                    """;
                File.WriteAllText(actualPath, minimalVl.Trim());

                var loadMethod = session.GetType().GetMethod("LoadDocumentInBackground",
                    BindingFlags.Public | BindingFlags.Instance);
                if (loadMethod is not null)
                {
                    PostToUIThread(() => loadMethod.Invoke(session, new object[] { actualPath }));
                    return new { success = true, filePath = actualPath, method = "created+loaded" };
                }
            }

            return new { success = false, error = "Could not create document" };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }

    private object HandleCloseDocument(HttpListenerRequest request)
    {
        var body = ReadBody(request);
        var doc = JsonDocument.Parse(body).RootElement;
        var filePath = doc.TryGetProperty("filePath", out var fp) ? fp.GetString() : null;
        var save = doc.TryGetProperty("save", out var sv) && sv.GetBoolean();

        if (string.IsNullOrEmpty(filePath))
            return new { success = false, error = "filePath required" };

        try
        {
            var session = GetSession();
            if (session is null)
                return new { success = false, error = "Session not available" };

            // Find document by file path to get its ID
            var solution = session.GetType().GetProperty("CurrentSolution")?.GetValue(session);
            if (solution is null)
                return new { success = false, error = "No solution" };

            var docsEnum = solution.GetType().GetProperty("Documents")?.GetValue(solution) as IEnumerable;
            if (docsEnum is null)
                return new { success = false, error = "No documents" };

            object? targetDoc = null;
            uint docId = 0;
            foreach (var d in docsEnum)
            {
                var fp2 = d.GetType().GetProperty("FilePath")?.GetValue(d)?.ToString();
                if (string.Equals(fp2, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    targetDoc = d;
                    // Try multiple ways to get the document's uint ID for CloseDocument
                    // 1. Try DocumentId Identity property
                    // 2. Try ElementId Identity property  
                    // 3. Try implicit uint conversion
                    foreach (var prop in d.GetType().GetProperties()
                        .Where(p => p.Name == "Identity"))
                    {
                        try
                        {
                            var identity = prop.GetValue(d);
                            if (identity is null) continue;
                            
                            // Check if it IS a uint directly
                            if (identity is uint u) { docId = u; break; }
                            
                            // Check for Value property
                            var valProp = identity.GetType().GetProperty("Value");
                            if (valProp is not null)
                            {
                                var val = valProp.GetValue(identity);
                                if (val is uint uv) { docId = uv; break; }
                                if (val is int iv) { docId = (uint)iv; break; }
                            }
                            
                            // Check for Id property
                            var idProp = identity.GetType().GetProperty("Id");
                            if (idProp is not null)
                            {
                                var val = idProp.GetValue(identity);
                                if (val is uint uv2) { docId = uv2; break; }
                            }

                            // Try fields (structs often have fields)
                            foreach (var field in identity.GetType().GetFields(
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                            {
                                var fval = field.GetValue(identity);
                                if (fval is uint uf) { docId = uf; break; }
                            }
                            if (docId > 0) break;

                            // Last resort: parse ToString
                            if (uint.TryParse(identity.ToString(), out var parsed))
                            { docId = parsed; break; }
                        }
                        catch { continue; }
                    }
                    break;
                }
            }

            if (targetDoc is null)
                return new { success = false, error = $"Document not found: {filePath}" };

            // Save first if requested
            if (save)
            {
                var saveMethod = session.GetType().GetMethod("Save",
                    BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { targetDoc.GetType() }, null);
                if (saveMethod is not null)
                    PostToUIThread(() => saveMethod.Invoke(session, new[] { targetDoc }));
            }

            // Close: CloseDocument(uint id, bool showDialogIfChanged)
            var closeMethod = session.GetType().GetMethod("CloseDocument",
                BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(uint), typeof(bool) }, null);

            if (closeMethod is not null && docId > 0)
            {
                PostToUIThread(() => closeMethod.Invoke(session, new object[] { docId, false }));
                return new { success = true, filePath, closed = true, docId };
            }

            return new { success = false, error = $"CloseDocument: docId={docId}, method found={closeMethod is not null}" };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }

    private object HandleSaveDocument(HttpListenerRequest request)
    {
        var body = ReadBody(request);
        var filePath = JsonDocument.Parse(body).RootElement.GetProperty("filePath").GetString();
        if (string.IsNullOrEmpty(filePath))
            return new { success = false, error = "filePath required" };

        try
        {
            var session = GetSession();
            if (session is null)
                return new { success = false, error = "Session not available" };

            var solution = session.GetType().GetProperty("CurrentSolution")?.GetValue(session);
            var docsEnum = solution?.GetType().GetProperty("Documents")?.GetValue(solution) as IEnumerable;
            if (docsEnum is null)
                return new { success = false, error = "No documents" };

            object? targetDoc = null;
            foreach (var d in docsEnum)
            {
                var fp2 = d.GetType().GetProperty("FilePath")?.GetValue(d)?.ToString();
                if (string.Equals(fp2, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    targetDoc = d;
                    break;
                }
            }

            if (targetDoc is null)
                return new { success = false, error = $"Document not found: {filePath}" };

            var saveMethod = session.GetType().GetMethod("Save",
                BindingFlags.Public | BindingFlags.Instance,
                null, new[] { targetDoc.GetType() }, null);

            if (saveMethod is not null)
            {
                PostToUIThread(() => saveMethod.Invoke(session, new[] { targetDoc }));
                return new { success = true, filePath, saved = true };
            }

            return new { success = false, error = "Save method not found" };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }

    private object HandleSaveAll()
    {
        try
        {
            var session = GetSession();
            if (session is null)
                return new { success = false, error = "Session not available" };

            var method = session.GetType().GetMethod("SaveAllDocuments",
                BindingFlags.Public | BindingFlags.Instance);
            if (method is not null)
            {
                PostToUIThread(() => method.Invoke(session, Array.Empty<object>()));
                return new { success = true, message = "SaveAllDocuments called" };
            }

            return new { success = false, error = "SaveAllDocuments not found" };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }

    private object HandleReload(HttpListenerRequest request)
    {
        var body = ReadBody(request);
        var filePath = JsonDocument.Parse(body).RootElement.GetProperty("filePath").GetString();
        if (string.IsNullOrEmpty(filePath))
            return new { success = false, error = "filePath required" };

        if (!File.Exists(filePath))
            return new { success = false, error = $"File not found: {filePath}" };

        // Touch file to trigger vvvv's file watcher
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow);
        return new { success = true, filePath };
    }

    // ── Log/Console ──────────────────────────────────────────────────────────────

    private object HandleGetLog(HttpListenerRequest request)
    {
        var limitStr = request.QueryString["limit"];
        var limit = int.TryParse(limitStr, out var l) ? l : 50;
        var severity = request.QueryString["severity"];

        var entries = _logCapture.GetEntries(limit, severity);
        return new
        {
            count = entries.Count,
            entries = entries.Select(e => new
            {
                timestamp = e.Timestamp,
                severity = e.Severity,
                category = e.Category,
                message = e.Message,
                exception = e.Exception
            })
        };
    }

    private object HandleClearLog()
    {
        _logCapture.Clear();
        return new { success = true, message = "Log cleared" };
    }

    // ── Navigation ───────────────────────────────────────────────────────────────

    private object HandleNavigate(HttpListenerRequest request)
    {
        var body = ReadBody(request);
        var doc = JsonDocument.Parse(body).RootElement;
        var filePath = doc.TryGetProperty("filePath", out var fp) ? fp.GetString() : null;

        if (string.IsNullOrEmpty(filePath))
            return new { success = false, error = "filePath required" };

        try
        {
            var session = GetSession();
            if (session is null)
                return new { success = false, error = "Session not available" };

            // Check if document is already open
            var solution = session.GetType().GetProperty("CurrentSolution")?.GetValue(session);
            var docsEnum = solution?.GetType().GetProperty("Documents")?.GetValue(solution) as IEnumerable;
            bool isOpen = false;
            if (docsEnum is not null)
            {
                foreach (var d in docsEnum)
                {
                    var fp2 = d.GetType().GetProperty("FilePath")?.GetValue(d)?.ToString();
                    if (string.Equals(fp2, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        isOpen = true;
                        break;
                    }
                }
            }

            if (!isOpen)
            {
                // Open it first
                var loadMethod = session.GetType().GetMethod("LoadDocumentInBackground",
                    BindingFlags.Public | BindingFlags.Instance);
                if (loadMethod is not null)
                    PostToUIThread(() => loadMethod.Invoke(session, new object[] { filePath! }));
            }

            // TODO: Once open, navigate to a specific node if nodeId is provided
            // This would require MainForm access via SynchronizationContext
            // For now, opening the document is enough - vvvv brings it to front

            return new { success = true, filePath, wasOpen = isOpen, action = isOpen ? "focused" : "opened" };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private object? GetSession()
    {
        try
        {
            var vlLangAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "VL.Lang");
            if (vlLangAsm is null) return null;

            var sessionType = vlLangAsm.GetType("VL.Model.VLSession");
            var instanceProp = sessionType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            return instanceProp?.GetValue(null);
        }
        catch { return null; }
    }

    private void PostToUIThread(Action action)
    {
        // Use SynchronizationContext to marshal to the vvvv main/UI thread
        var syncCtx = _nodeContext?.AppHost.SynchronizationContext;
        if (syncCtx is not null)
        {
            syncCtx.Post(_ => { try { action(); } catch { } }, null);
        }
        else
        {
            // Fallback: run directly (may cause cross-thread issues for UI operations)
            action();
        }
    }

    private static string ReadBody(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        return reader.ReadToEnd();
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

    // ── Debug (kept from before) ─────────────────────────────────────────────────

    private object HandleDebug()
    {
        var info = new Dictionary<string, object?>();
        try
        {
            var session = GetSession();
            info["sessionFound"] = session is not null;
            info["logEntriesCaptured"] = _logCapture.GetEntries(1000).Count;
            info["documentsTracked"] = _state.Documents.Count;
            info["errorsTracked"] = _state.Errors.Count;
            info["packagesTracked"] = _state.Packages.Count;
            info["frameCount"] = _state.FrameCount;
        }
        catch (Exception ex)
        {
            info["exception"] = ex.Message;
        }
        return info;
    }

    private object HandleExplore(HttpListenerRequest request)
    {
        var info = new Dictionary<string, object?>();
        try
        {
            var queryPath = request.QueryString["path"] ?? "";
            info["path"] = queryPath;

            var vlLangAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "VL.Lang");
            if (vlLangAsm is null) return new { error = "VL.Lang not loaded" };

            var sessionType = vlLangAsm.GetType("VL.Model.VLSession");
            if (sessionType is null) return new { error = "VLSession type not found" };

            var instanceProp = sessionType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var current = instanceProp?.GetValue(null);
            if (current is null) return new { error = "VLSession.Instance is null" };

            info["rootType"] = current.GetType().FullName;

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
                            .Take(50).ToArray();
                        return info;
                    }
                    current = prop.GetValue(current);
                    info["resolvedType"] = current?.GetType().FullName;
                }
            }

            if (current is null) { info["value"] = null; return info; }

            info["type"] = current.GetType().FullName;
            info["properties"] = current.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => $"{p.Name} : {p.PropertyType.FullName}")
                .Take(60).ToArray();

            var showMethods = request.QueryString["methods"];
            if (showMethods == "true" || showMethods == "1")
            {
                var flags = BindingFlags.Public | BindingFlags.Instance;
                if (request.QueryString["declared"] == "true")
                    flags |= BindingFlags.DeclaredOnly;

                info["methods"] = current.GetType().GetMethods(flags)
                    .Where(m => !m.IsSpecialName)
                    .Select(m => $"{m.DeclaringType?.Name}.{m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))}) : {m.ReturnType.Name}")
                    .Distinct().Take(120).ToArray();
            }

            if (request.QueryString["interfaces"] == "true")
            {
                info["interfaces"] = current.GetType().GetInterfaces()
                    .Select(i => i.FullName).Take(30).ToArray();
            }

            if (current is IEnumerable enumerable && current is not string)
            {
                var items = new List<object?>();
                int count = 0;
                foreach (var item in enumerable)
                {
                    if (count >= 10) { items.Add("...(truncated)"); break; }
                    var itemType = item?.GetType();
                    var nameP = itemType?.GetProperty("Name")?.GetValue(item)?.ToString();
                    var pathP = itemType?.GetProperty("FilePath")?.GetValue(item)?.ToString()
                             ?? itemType?.GetProperty("Path")?.GetValue(item)?.ToString();
                    var msgP = itemType?.GetProperty("Message")?.GetValue(item)?.ToString()
                             ?? itemType?.GetProperty("Text")?.GetValue(item)?.ToString();
                    if (nameP is not null || pathP is not null || msgP is not null)
                        items.Add(new { type = itemType?.Name, name = nameP, path = pathP, message = msgP });
                    else
                        items.Add(new { type = itemType?.Name, value = item?.ToString() });
                    count++;
                }
                info["items"] = items;
                info["itemCount"] = count;

                foreach (var item in enumerable)
                {
                    if (item is not null)
                    {
                        info["itemType"] = item.GetType().FullName;
                        info["itemProperties"] = item.GetType()
                            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Select(p => $"{p.Name} : {p.PropertyType.Name}")
                            .Take(40).ToArray();
                    }
                    break;
                }
            }
            else
            {
                info["valueStr"] = current.ToString();
            }
        }
        catch (Exception ex) { info["exception"] = ex.Message; }
        return info;
    }
}

internal record ReloadRequest(string? FilePath);
