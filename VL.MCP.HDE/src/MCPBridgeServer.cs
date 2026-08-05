using System.Collections;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using VL.Core;
using VL.Core.Import;

namespace VL.MCP;

/// <summary>Single source of truth for the bridge version (REST + MCP).</summary>
internal static class BridgeVersion
{
    public const string Current = "0.3.0";
}

/// <summary>
/// Process node that runs an HTTP bridge server inside vvvv.
/// Exposes document management, error monitoring, log capture, and editor navigation.
/// </summary>
    [ProcessNode]
    public class MCPBridgeServer : IDisposable
    {
    private HttpListener? _listener;
    private Task? _serverTask;
    private CancellationTokenSource? _cts;
    private int _currentPort;
    private NodeContext? _nodeContext;
    private readonly BridgeState _state = new();
    private readonly BridgeLogCapture _logCapture = new();
    private bool _logProviderRegistered;
    private bool _consoleTeeInstalled;
    private McpSseServer? _mcpSse;
    private McpChatHost?  _chatHost;
    private readonly LiveNodeCatalog _nodeCatalog = new();
    private readonly InProcessTools _inProcess = new();
    // Chat toggle — rising edge of openChat flips _chatEnabled, so both
    // a momentary bang and a persistent bool work as input
    private bool _chatEnabled;
    private bool _prevOpenChat;
    
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public (int Port, bool IsRunning, string? LastError, bool ChatIsReady) Update(
        NodeContext nodeContext,
        int port = 7123,
        bool enabled = true,
        bool openChat = false,
        int chatPort = 7125)
    {
        _nodeContext = nodeContext;
        string? lastError = null;

        // Env var overrides allow running multiple vvvv instances side by side
        // (e.g. a dev instance next to a production one). The MCP client side
        // (BridgeClientService) honors the same variables.
        if (Environment.GetEnvironmentVariable("VVVV_MCP_BRIDGE_PORT") is { } bp &&
            int.TryParse(bp, out var envPort))
            port = envPort;
        if (Environment.GetEnvironmentVariable("VVVV_MCP_CHAT_PORT") is { } cp &&
            int.TryParse(cp, out var envChatPort))
            chatPort = envChatPort;

        if (!enabled)
        {
            Stop();
            return (port, false, null, false);
        }

        if (port != _currentPort && _listener is not null)
            Stop();

        if (_listener is null && enabled)
        {
            _currentPort = port;
            lastError = Start();
        }

        // Register ILogger capture provider (once)
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

        // Tee Console.Out/Error into the same capture buffer (once)
        // This picks up System.Console entries: [OpenWebUI] messages, vvvv Sys log, etc.
        if (!_consoleTeeInstalled)
        {
            try
            {
                Console.SetOut(new ConsoleTee(Console.Out,   _logCapture, Microsoft.Extensions.Logging.LogLevel.Information, "System.Console"));
                Console.SetError(new ConsoleTee(Console.Error, _logCapture, Microsoft.Extensions.Logging.LogLevel.Warning,     "System.Console.Error"));
                _consoleTeeInstalled = true;
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
            _nodeCatalog.Update(nodeContext);
        }
        catch { }

        var isRunning = _listener is not null && _listener.IsListening;

        // Rising edge of openChat toggles _chatEnabled — works with both
        // a persistent bool (Toggle node) and a momentary bang (Command.On Execute)
        if (openChat && !_prevOpenChat) _chatEnabled = !_chatEnabled;
        _prevOpenChat = openChat;

        // Drive chat host — server must be up first
        var chatState = (IsReady: false, IsStarting: false, LastError: (string?)null, ChatUrl: $"http://localhost:{chatPort}");
        if (isRunning)
        {
            _chatHost ??= new McpChatHost();
            chatState = _chatHost.Update(_chatEnabled, chatPort, port);
        }

        return (port, isRunning, lastError, chatState.IsReady);
    }

    private string? Start()
    {
        try
        {
            _cts      = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{_currentPort}/");
            _listener.Start();
            _inProcess.SetBridgePort(_currentPort);
            _mcpSse   = new McpSseServer(Dispatch);
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

    private async Task HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            var path = request.Url?.AbsolutePath ?? "/";
            var method = request.HttpMethod;

            // ── MCP routes ────────────────────────────────────────────────────────
            // Streamable HTTP (POST /mcp) — Open WebUI + 2025+ MCP clients
            if (path == "/mcp" && (method == "POST" || method == "OPTIONS"))
            {
                await _mcpSse!.HandleStreamableHttpAsync(context, _cts?.Token ?? CancellationToken.None);
                return;
            }
            // HTTP/SSE legacy transport (kept for compatibility)
            if (path == "/mcp/sse" && method == "GET")
            {
                await _mcpSse!.HandleSseAsync(context, _cts?.Token ?? CancellationToken.None);
                return;
            }
            if (path == "/mcp/message" && method == "POST")
            {
                await _mcpSse!.HandleMessageAsync(context, _cts?.Token ?? CancellationToken.None);
                return;
            }

            // ── Chat placeholder / redirect ──
            if (path == "/chat" && method == "GET")
            {
                // If Open WebUI is already up, go straight there (302) — much more
                // robust than client-side polling from inside CEF. Otherwise serve
                // the friendly "setting up" page which keeps polling as a backup.
                var chatUrl = _chatHost?.ChatUrl ?? "http://localhost:7125";
                if (await IsChatUpAsync(chatUrl))
                {
                    response.StatusCode = 302;
                    response.RedirectLocation = chatUrl;
                    response.Close();
                    return;
                }
                WriteHtml(response, ChatPlaceholderPage.Html(_chatHost?.Status ?? "starting…"));
                return;
            }

            // ── Chat readiness probe (same-origin, used by the placeholder page) ──
            if (path == "/api/chat/status" && method == "GET")
            {
                var chatUrl = _chatHost?.ChatUrl ?? "http://localhost:7125";
                var up = await IsChatUpAsync(chatUrl);
                // Report ACTUAL reachability, not just the host's internal start state —
                // Open WebUI may be running even when this host didn't start it (adopted
                // or started externally). The placeholder only cares "can I redirect yet?".
                WriteJson(response, new
                {
                    ready = up,
                    url = chatUrl,
                    status = up ? "ready" : (_chatHost?.Status ?? "setting up…"),
                    error = up ? null : _chatHost?.LastError
                });
                return;
            }

            object? result = (method, path) switch
            {
                // ── Status ──
                ("GET", "/api/ping") => new
                {
                    status = "ok",
                    server = "VL.MCP",
                    version = BridgeVersion.Current,
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
                ("GET", "/api/channels") => _state.Channels,

                // ── Document Operations ──
                ("POST", "/api/documents/open") => HandleOpenDocument(request),
                ("POST", "/api/documents/new") => HandleNewDocument(request),
                ("POST", "/api/documents/close") => HandleCloseDocument(request),
                ("POST", "/api/documents/save") => HandleSaveDocument(request),
                ("POST", "/api/documents/save-all") => HandleSaveAll(),
                ("POST", "/api/reload") => HandleReload(request),
                ("POST", "/api/pin/set") => HandleSetPinValue(request),

                // ── Editor / Tabs ──
                ("GET", "/api/tabs") => HandleGetTabs(),
                ("POST", "/api/tabs/close") => HandleCloseTab(request),
                ("POST", "/api/undo") => HandleUndo(request),
                ("POST", "/api/redo") => HandleRedo(request),

                // ── Log/Console ──
                ("GET", "/api/log") => HandleGetLog(request),
                ("DELETE", "/api/log") => HandleClearLog(),

                // ── Live node catalog ──
                ("GET", "/api/nodes") => HandleNodeSearch(request),
                ("GET", "/api/nodes/lookup") => HandleNodeLookup(request),
                ("GET", "/api/nodes/categories") => _nodeCatalog.GetCategories(request.QueryString["prefix"]),
                ("GET", "/api/nodes/stats") => HandleNodeStats(request),

                // ── Navigation ──
                ("POST", "/api/navigate") => HandleNavigate(request),

                // ── UI Thread exploration ──
                ("GET", "/api/debug/mainform") => HandleMainFormExplore(),

                // ── Debug ──
                ("GET", "/api/debug") => HandleDebug(),
                ("GET", "/api/chat-state") => new
                {
                    chatEnabled   = _chatEnabled,
                    prevOpenChat  = _prevOpenChat,
                    chatHostReady = _chatHost is not null,
                    chatLastError = _chatHost?.LastError,
                    chatIsReady   = _chatHost?.IsReady ?? false,
                },
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

            if (existingDoc is not null)
            {
                // Document in solution — use ShowDocument on EditorControl (UI thread)
                var shown = ShowDocumentOnUIThread(session, existingDoc);
                if (shown)
                    return new { success = true, filePath, method = "EditorControl.ShowDocument" };
            }

            // Document not in solution — load it first, then show
            var loadMethod = session.GetType().GetMethod("LoadDocumentInBackground",
                BindingFlags.Public | BindingFlags.Instance);
            if (loadMethod is not null)
            {
                var resultObj = new { success = true, filePath, method = "LoadDocumentInBackground+ShowDocument" };
                PostToUIThread(() =>
                {
                    try
                    {
                        loadMethod.Invoke(session, new object[] { filePath! });
                        // Give it a moment to load, then show
                        Task.Delay(500).ContinueWith(_ =>
                        {
                            _nodeContext?.AppHost.SynchronizationContext?.Post(__ =>
                            {
                                var sol = session.GetType().GetProperty("CurrentSolution")?.GetValue(session);
                                var docs = sol?.GetType().GetProperty("Documents")?.GetValue(sol) as IEnumerable;
                                if (docs is not null)
                                {
                                    foreach (var d in docs)
                                    {
                                        var fp2 = d.GetType().GetProperty("FilePath")?.GetValue(d)?.ToString();
                                        if (string.Equals(fp2, filePath, StringComparison.OrdinalIgnoreCase))
                                        {
                                            ShowDocumentOnUIThread(session, d);
                                            break;
                                        }
                                    }
                                }
                            }, null);
                        });
                    }
                    catch { }
                });
                return resultObj;
            }

            // Last resort: shell execute
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath!)
            { UseShellExecute = true });
            return new { success = true, filePath, method = "ShellExecute (fallback)" };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }

    /// <summary>
    /// Call EditorControl.ShowDocument on the UI thread. Returns true if successful.
    /// Must be called from the UI thread OR via PostToUIThread.
    /// </summary>
    private bool ShowDocumentOnUIThread(object session, object document)
    {
        var done = new ManualResetEventSlim(false);
        bool success = false;

        PostToUIThread(() =>
        {
            try
            {
                var mainForm = session.GetType().GetProperty("MainForm")?.GetValue(session);
                var editorControl = mainForm?.GetType().GetProperty("EditorControl")?.GetValue(mainForm);
                if (editorControl is null) { done.Set(); return; }

                // ShowDocument(Document document, ShowSpecialPatch showSpecialPatch)
                // ShowSpecialPatch is likely an enum - try passing 0 (default/None)
                var showMethod = editorControl.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "ShowDocument" && m.GetParameters().Length == 2);

                if (showMethod is not null)
                {
                    var paramType = showMethod.GetParameters()[1].ParameterType;
                    var defaultVal = Enum.ToObject(paramType, 0); // first enum value
                    showMethod.Invoke(editorControl, new[] { document, defaultVal });
                    success = true;
                }
            }
            catch { }
            finally { done.Set(); }
        });

        done.Wait(TimeSpan.FromSeconds(3));
        return success;
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

        // 1. If the document is open in the session, reload it properly from disk.
        //    This updates the in-memory model AND the editor UI. Touching the file
        //    alone does NOT work — vvvv does not watch arbitrary files.
        //    Safe to block here: we are on a threadpool thread, the reload itself
        //    is marshaled to the vvvv main thread inside.
        DocumentReloadResult result;
        try
        {
            result = _state.ReloadDocumentFromDiskAsync(filePath, _nodeContext?.AppHost)
                           .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return new { success = false, filePath, error = ex.Message };
        }

        if (result.Found)
        {
            return new
            {
                success = result.Reloaded,
                filePath,
                method = "Document.ReloadAsync",
                discardedEditorChanges = result.HadUnsavedChanges ? true : (bool?)null,
                error = result.Error
            };
        }

        // 2. Not open in the session — touch the file as a fallback
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow);
        return new { success = true, filePath, method = "touch (document not open in session)" };
    }

    /// <summary>
    /// POST /api/pin/set — set a pin's default value on the LIVE model (editor API,
    /// undo-integrated, no reload). Body: { elementId, pinName, value, documentId?,
    /// filePath?, typeHint? }. documentId is resolved from filePath when only that is given.
    /// </summary>
    private object HandleSetPinValue(HttpListenerRequest request)
    {
        try
        {
            var body = ReadBody(request);
            using var jsonDoc = JsonDocument.Parse(body);
            var root = jsonDoc.RootElement;

            string? Str(string key) =>
                root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            var elementId = Str("elementId");
            var pinName = Str("pinName");
            var value = Str("value");
            var documentId = Str("documentId");
            var filePath = Str("filePath");
            var typeHint = Str("typeHint");

            if (string.IsNullOrEmpty(elementId) || string.IsNullOrEmpty(pinName) || value is null)
                return new { success = false, error = "elementId, pinName and value are required" };

            // Resolve documentId from the .vl file's Document Id when only filePath is given
            if (string.IsNullOrEmpty(documentId) && !string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                try { documentId = XDocument.Load(filePath).Root?.Attribute("Id")?.Value; } catch { }
            }
            if (string.IsNullOrEmpty(documentId))
                return new { success = false, error = "documentId is required (or pass filePath to resolve it)" };

            return _state.SetPinValueLiveAsync(documentId, elementId, pinName, value, typeHint, _nodeContext?.AppHost)
                         .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
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

    // ── Live node catalog ─────────────────────────────────────────────────────

    private object HandleNodeSearch(HttpListenerRequest request)
    {
        if (request.QueryString["refresh"] == "1" || request.QueryString["refresh"] == "true")
            _nodeCatalog.RequestRebuild();

        var query = request.QueryString["query"];
        var category = request.QueryString["category"];
        var limitStr = request.QueryString["limit"];
        var limit = int.TryParse(limitStr, out var l) ? l : 30;
        var pins = request.QueryString["pins"];
        var includePins = pins == "1" || pins == "true";

        return _nodeCatalog.Search(query, category, limit, includePins);
    }

    private object HandleNodeLookup(HttpListenerRequest request)
    {
        if (request.QueryString["refresh"] == "1" || request.QueryString["refresh"] == "true")
            _nodeCatalog.RequestRebuild();

        var name = request.QueryString["name"];
        if (string.IsNullOrEmpty(name))
            return new { found = false, error = "name parameter required" };

        var category = request.QueryString["category"];
        return _nodeCatalog.Lookup(name, category);
    }

    private object HandleNodeStats(HttpListenerRequest request)
    {
        if (request.QueryString["refresh"] == "1" || request.QueryString["refresh"] == "true")
            _nodeCatalog.RequestRebuild();

        return new
        {
            nodes = _nodeCatalog.NodeCount,
            builtAt = _nodeCatalog.BuiltAt,
            stale = _nodeCatalog.IsStale,
            lastError = _nodeCatalog.LastBuildError,
            diagnostics = _nodeCatalog.Diagnostics
        };
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

    // ── Tabs / Editor ────────────────────────────────────────────────────────────

    private object HandleGetTabs()
    {
        var result = new Dictionary<string, object?>();
        var done = new ManualResetEventSlim(false);

        PostToUIThread(() =>
        {
            try
            {
                var session = GetSession();
                var mainForm = session?.GetType().GetProperty("MainForm")?.GetValue(session);
                var editorControl = mainForm?.GetType().GetProperty("EditorControl")?.GetValue(mainForm);
                if (editorControl is null) { result["error"] = "EditorControl not available"; done.Set(); return; }

                // Get open patches (tabs)
                var openPatches = editorControl.GetType().GetProperty("OpenPatches")?.GetValue(editorControl) as IEnumerable;
                var tabs = new List<object>();
                if (openPatches is not null)
                {
                    foreach (var patch in openPatches)
                    {
                        var pType = patch.GetType();
                        var name = pType.GetProperty("Name")?.GetValue(patch)?.ToString();
                        var filePath = pType.GetProperty("FilePath")?.GetValue(patch)?.ToString();
                        tabs.Add(new { name, filePath, type = pType.Name });
                    }
                }
                result["tabs"] = tabs;
                result["count"] = tabs.Count;

                // Get active canvas/tab
                var activeWindow = editorControl.GetType().GetProperty("ActiveCanvasWindow")?.GetValue(editorControl);
                if (activeWindow is not null)
                {
                    var awType = activeWindow.GetType();
                    var activeName = awType.GetProperty("Text")?.GetValue(activeWindow)?.ToString()
                                 ?? awType.GetProperty("Name")?.GetValue(activeWindow)?.ToString();
                    result["activeTab"] = activeName;
                }

                // Get selected canvas
                var selectedCanvas = editorControl.GetType().GetProperty("SelectedCanvas")?.GetValue(editorControl);
                if (selectedCanvas is not null)
                {
                    var scType = selectedCanvas.GetType();
                    var canvasName = scType.GetProperty("Name")?.GetValue(selectedCanvas)?.ToString();
                    var canvasId = scType.GetProperty("SerializedId")?.GetValue(selectedCanvas)?.ToString()
                               ?? scType.GetProperty("Id")?.GetValue(selectedCanvas)?.ToString();
                    result["selectedCanvas"] = new { name = canvasName, id = canvasId };
                }
            }
            catch (Exception ex) { result["error"] = ex.Message; }
            finally { done.Set(); }
        });

        done.Wait(TimeSpan.FromSeconds(5));
        return result;
    }

    private object HandleCloseTab(HttpListenerRequest request)
    {
        var body = ReadBody(request);
        var doc = JsonDocument.Parse(body).RootElement;
        var filePath = doc.TryGetProperty("filePath", out var fp) ? fp.GetString() : null;
        var canvasId = doc.TryGetProperty("canvasId", out var cid) ? cid.GetString() : null;

        if (string.IsNullOrEmpty(filePath))
            return new { success = false, error = "filePath required" };

        var result = new Dictionary<string, object?>();
        var done = new ManualResetEventSlim(false);

        PostToUIThread(() =>
        {
            try
            {
                var session = GetSession();
                var mainForm = session?.GetType().GetProperty("MainForm")?.GetValue(session);
                var editorControl = mainForm?.GetType().GetProperty("EditorControl")?.GetValue(mainForm);
                if (editorControl is null) { result["error"] = "EditorControl not available"; done.Set(); return; }

                // CloseCanvas(string filePath, string canvasId)
                var closeMethod = editorControl.GetType().GetMethod("CloseCanvas",
                    new[] { typeof(string), typeof(string) });

                if (closeMethod is not null)
                {
                    closeMethod.Invoke(editorControl, new object?[] { filePath, canvasId });
                    result["success"] = true;
                    result["filePath"] = filePath;
                }
                else
                {
                    result["success"] = false;
                    result["error"] = "CloseCanvas(string,string) not found";
                }
            }
            catch (Exception ex) { result["error"] = ex.Message; result["success"] = false; }
            finally { done.Set(); }
        });

        done.Wait(TimeSpan.FromSeconds(3));
        return result;
    }

    private object HandleUndo(HttpListenerRequest request)
    {
        return HandleUndoRedo(request, isUndo: true);
    }

    private object HandleRedo(HttpListenerRequest request)
    {
        return HandleUndoRedo(request, isUndo: false);
    }

    private object HandleUndoRedo(HttpListenerRequest request, bool isUndo)
    {
        var result = new Dictionary<string, object?>();
        var done = new ManualResetEventSlim(false);

        PostToUIThread(() =>
        {
            try
            {
                var session = GetSession();
                if (session is null) { result["error"] = "Session not available"; done.Set(); return; }

                // Get the active canvas from EditorControl
                var mainForm = session.GetType().GetProperty("MainForm")?.GetValue(session);
                var editorControl = mainForm?.GetType().GetProperty("EditorControl")?.GetValue(mainForm);
                var selectedCanvas = editorControl?.GetType().GetProperty("SelectedCanvas")?.GetValue(editorControl);

                if (selectedCanvas is null)
                {
                    result["success"] = false;
                    result["error"] = "No active canvas";
                    done.Set();
                    return;
                }

                var methodName = isUndo ? "Undo" : "Redo";
                var canMethodName = isUndo ? "CanUndo" : "CanRedo";

                // Check if undo/redo is possible
                var canMethod = session.GetType().GetMethod(canMethodName,
                    BindingFlags.Public | BindingFlags.Instance);
                var canDo = canMethod?.Invoke(session, new[] { selectedCanvas }) as bool? ?? false;

                if (!canDo)
                {
                    result["success"] = false;
                    result["error"] = $"Cannot {methodName} on active canvas";
                    done.Set();
                    return;
                }

                // Perform undo/redo
                var method = session.GetType().GetMethod(methodName,
                    BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { selectedCanvas.GetType() }, null);

                if (method is not null)
                {
                    method.Invoke(session, new[] { selectedCanvas });
                    result["success"] = true;
                    result["action"] = methodName;
                }
                else
                {
                    // Try with base Canvas type
                    var canvasType = selectedCanvas.GetType().BaseType;
                    while (canvasType is not null)
                    {
                        method = session.GetType().GetMethod(methodName,
                            BindingFlags.Public | BindingFlags.Instance,
                            null, new[] { canvasType }, null);
                        if (method is not null) break;
                        canvasType = canvasType.BaseType;
                    }

                    if (method is not null)
                    {
                        method.Invoke(session, new[] { selectedCanvas });
                        result["success"] = true;
                        result["action"] = methodName;
                    }
                    else
                    {
                        result["success"] = false;
                        result["error"] = $"{methodName} method not found for canvas type";
                    }
                }
            }
            catch (Exception ex) { result["error"] = ex.Message; result["success"] = false; }
            finally { done.Set(); }
        });

        done.Wait(TimeSpan.FromSeconds(3));
        return result;
    }

    // ── UI Thread MainForm Exploration ─────────────────────────────────────────

    private object HandleMainFormExplore()
    {
        var result = new Dictionary<string, object?>();
        var done = new ManualResetEventSlim(false);

        // Must run on UI thread to safely access MainForm
        PostToUIThread(() =>
        {
            try
            {
                var session = GetSession();
                if (session is null) { result["error"] = "No session"; done.Set(); return; }

                var mainFormProp = session.GetType().GetProperty("MainForm",
                    BindingFlags.Public | BindingFlags.Instance);
                var mainForm = mainFormProp?.GetValue(session);

                if (mainForm is null) { result["error"] = "MainForm is null"; done.Set(); return; }

                result["mainFormType"] = mainForm.GetType().FullName;

                // Get EditorControl
                var editorControlProp = mainForm.GetType().GetProperty("EditorControl",
                    BindingFlags.Public | BindingFlags.Instance);
                var editorControl = editorControlProp?.GetValue(mainForm);

                if (editorControl is null) { result["error"] = "EditorControl is null"; done.Set(); return; }

                result["editorControlType"] = editorControl.GetType().FullName;

                // Get properties of EditorControl
                result["properties"] = editorControl.GetType()
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(p => $"{p.Name} : {p.PropertyType.Name}")
                    .Take(60)
                    .ToArray();

                // Get methods of EditorControl (declared only first)
                result["methods"] = editorControl.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsSpecialName)
                    .Select(m => $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))}) : {m.ReturnType.Name}")
                    .Take(100)
                    .ToArray();
            }
            catch (Exception ex)
            {
                result["exception"] = ex.Message;
            }
            finally
            {
                done.Set();
            }
        });

        // Wait for UI thread to complete (with timeout)
        done.Wait(TimeSpan.FromSeconds(5));
        if (result.Count == 0)
            result["error"] = "Timed out waiting for UI thread";

        return result;
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

    private static void WriteHtml(HttpListenerResponse response, string html)
    {
        var buffer = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        try { response.OutputStream.Write(buffer, 0, buffer.Length); } catch { }
    }

    /// <summary>True when Open WebUI answers on its URL (any non-5xx HTTP status).</summary>
    private static async Task<bool> IsChatUpAsync(string chatUrl)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(2500) };
            // Base url ("/") — some OWUI versions don't have /health.
            var resp = await client.GetAsync(chatUrl);
            return (int)resp.StatusCode < 500;
        }
        catch { return false; }
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
        try { _listener?.Stop(); }  catch { }
        try { _listener?.Close(); } catch { }
        _listener   = null;
        _cts?.Dispose();
        _cts        = null;
        _serverTask = null;
        _chatHost?.Dispose();
        _chatHost   = null;
    }

    /// <summary>
    /// Critical: release the HTTP listener when vvvv disposes this node instance
    /// (document reload, C# recompile, patch close). Without this the old listener
    /// keeps port 7123 hostage and the recompiled bridge can never start.
    /// </summary>
    public void Dispose()
    {
        Stop();
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
                foreach (var rawPart in parts)
                {
                    if (current is null) break;

                    // Support enumerable indexing: "DocumentSymbols[3]"
                    var part = rawPart;
                    var index = -1;
                    var bracket = rawPart.IndexOf('[');
                    if (bracket > 0 && rawPart.EndsWith(']'))
                    {
                        if (int.TryParse(rawPart[(bracket + 1)..^1], out var idx))
                        {
                            index = idx;
                            part = rawPart[..bracket];
                        }
                    }

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

                    if (index >= 0 && current is IEnumerable enumerable2 && current is not string)
                    {
                        var i = 0;
                        object? found = null;
                        foreach (var item in enumerable2)
                        {
                            if (i == index) { found = item; break; }
                            i++;
                        }
                        current = found;
                    }
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
                var take = int.TryParse(request.QueryString["take"], out var t) ? Math.Clamp(t, 1, 500) : 10;
                var items = new List<object?>();
                int count = 0;
                foreach (var item in enumerable)
                {
                    if (count >= take) { items.Add("...(truncated)"); break; }
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

    // ── MCP tool dispatch (used by McpSseServer) ──────────────────────────────

    private string Dispatch(string toolName, string paramsJson)
    {
        try
        {
            using var doc  = System.Text.Json.JsonDocument.Parse(
                string.IsNullOrWhiteSpace(paramsJson) ? "{}" : paramsJson);
            var p = doc.RootElement;

            string Str(string key, string def = "") =>
                p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString() ?? def : def;
            bool Bool(string key, bool def = false) =>
                p.TryGetProperty(key, out var v)
                    ? v.ValueKind == JsonValueKind.True  ? true
                    : v.ValueKind == JsonValueKind.False ? false : def
                    : def;
            int Int(string key, int def = 0) =>
                p.TryGetProperty(key, out var v) && v.TryGetInt32(out var i) ? i : def;

            object result = toolName switch
            {
                "check_bridge_connection"  => new { connected = true, runtime = new { _state.IsRunning, _state.IsPaused, _state.FrameCount, _state.UptimeSeconds } },
                "get_running_documents"    => _state.Documents,
                "get_vvvv_errors"          => new { summary = $"{_state.Errors.Count(e => e.Severity == "Error")} error(s)", errors = _state.Errors },
                "get_vvvv_state"           => new { _state.IsRunning, _state.IsPaused, _state.FrameCount, _state.UptimeSeconds },
                "get_vvvv_log"             => new { count = _logCapture.GetEntries(Int("limit", 50), Str("severity") is "" ? null : Str("severity")).Count, entries = _logCapture.GetEntries(Int("limit", 50), Str("severity") is "" ? null : Str("severity")) },
                "get_open_tabs"            => HandleGetTabs(),
                "open_document_in_vvvv"    => HandleOpenDocumentDirect(Str("filePath")),
                "close_document_in_vvvv"   => HandleCloseDocument(Str("filePath"), Bool("save")),
                "save_document_in_vvvv"    => HandleSaveDocumentDirect(Str("filePath")),
                "reload_file_in_vvvv"      => HandleReloadDirect(Str("filePath")),
                "undo_in_vvvv"             => HandleUndoDirect(),
                "redo_in_vvvv"             => HandleRedoDirect(),
                // ── Shared Core services (build_patch, live nodes, patch read) ──
                "build_patch" or "search_nodes_live" or "get_node_details_live"
                    or "refresh_live_nodes" or "read_patch" or "explain_patch"
                    or "list_patch_dependencies"
                    => _inProcess.DispatchAsync(toolName, paramsJson).GetAwaiter().GetResult(),
                _                          => (object)new { error = $"Unknown tool: {toolName}" }
            };

            return JsonSerializer.Serialize(result, JsonOpts);
        }
        catch (Exception ex) { return JsonSerializer.Serialize(new { error = ex.Message }, JsonOpts); }
    }

    // Direct (non-HTTP) versions for MCP dispatch
    private object HandleOpenDocumentDirect(string filePath)
    {
        if (!File.Exists(filePath)) return new { success = false, error = $"Not found: {filePath}" };
        var session = GetSession();
        if (session is null) return new { success = false, error = "Session not available" };
        var solution = session.GetType().GetProperty("CurrentSolution")?.GetValue(session);
        var docsEnum = solution?.GetType().GetProperty("Documents")?.GetValue(solution) as System.Collections.IEnumerable;
        object? found = null;
        if (docsEnum is not null)
            foreach (var d in docsEnum)
            {
                var fp = d.GetType().GetProperty("FilePath")?.GetValue(d)?.ToString();
                if (string.Equals(fp, filePath, StringComparison.OrdinalIgnoreCase)) { found = d; break; }
            }
        if (found is not null) { ShowDocumentOnUIThread(session, found); return new { success = true, filePath }; }
        var load = session.GetType().GetMethod("LoadDocumentInBackground",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (load is not null) { PostToUIThread(() => load.Invoke(session, [filePath])); return new { success = true, filePath }; }
        return new { success = false, error = "Could not open document" };
    }
    private object HandleSaveDocumentDirect(string filePath)
    {
        if (filePath.Equals("all", StringComparison.OrdinalIgnoreCase)) return HandleSaveAll();
        // TODO: add HandleSaveDocument(string filePath) overload in next iteration
        return new { success = false, error = "Use save_document_in_vvvv via the REST API for now" };
    }
    private object HandleCloseDocument(string filePath, bool save)
    {
        // TODO: add HandleCloseDocument(string filePath, bool save) overload in next iteration
        return new { success = false, error = "Use close_document_in_vvvv via the REST API for now" };
    }
    private object HandleReloadDirect(string filePath)
    {
        if (!File.Exists(filePath)) return new { success = false, error = $"Not found: {filePath}" };
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow);
        return new { success = true, filePath };
    }
    private object HandleUndoDirect() => HandleUndoRedo(null!, isUndo: true);
    private object HandleRedoDirect()  => HandleUndoRedo(null!, isUndo: false);
}

internal record ReloadRequest(string? FilePath);
