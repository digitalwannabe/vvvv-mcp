using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VL.MCP;

/// <summary>
/// MCP JSON-RPC 2.0 server with two transports:
///
///   Streamable HTTP (Open WebUI, 2025+ clients):
///     POST /mcp  — synchronous JSON request/response, no session state needed
///
///   HTTP/SSE (legacy clients):
///     GET  /mcp/sse     — SSE stream with 'endpoint' event
///     POST /mcp/message — JSON-RPC messages, response via SSE
/// </summary>
internal class McpSseServer
{
    private readonly Func<string, string, string> _dispatch;
    private readonly ConcurrentDictionary<string, SseSession> _sessions = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented        = false
    };

    public McpSseServer(Func<string, string, string> dispatch) => _dispatch = dispatch;

    // ── Streamable HTTP (POST /mcp) — Open WebUI native format ───────────────

    public async Task HandleStreamableHttpAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
        ctx.Response.AddHeader("Access-Control-Allow-Methods", "POST, OPTIONS");
        ctx.Response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization, Accept");

        if (ctx.Request.HttpMethod == "OPTIONS")
        {
            ctx.Response.StatusCode = 204;
            ctx.Response.Close();
            return;
        }

        string body;
        using (var sr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
            body = await sr.ReadToEndAsync(ct);

        try
        {
            var req      = JsonNode.Parse(body);
            var id       = req?["id"];
            var method   = req?["method"]?.GetValue<string>() ?? "";
            var params_  = req?["params"];

            object result = method switch
            {
                "initialize"   => (object)new
                {
                    protocolVersion = "2024-11-05",
                    capabilities    = new { tools = new { listChanged = false } },
                    serverInfo      = new { name = "vvvv-mcp-bridge", version = BridgeVersion.Current }
                },
                "tools/list"   => new { tools = ToolSchemas.LiveEditorTools },
                "tools/call"   => HandleToolCall(params_),
                "ping"         => new { },
                _              => (object)new { error = new { code = -32601, message = $"Method not found: {method}" } }
            };

            var json  = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }, JsonOpts);
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.ContentType     = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.StatusCode      = 200;
            await ctx.Response.OutputStream.WriteAsync(bytes, ct);
        }
        catch (Exception ex)
        {
            var err   = JsonSerializer.Serialize(new { jsonrpc = "2.0", error = new { code = -32603, message = ex.Message } }, JsonOpts);
            var bytes = Encoding.UTF8.GetBytes(err);
            ctx.Response.ContentType     = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.StatusCode      = 200;
            await ctx.Response.OutputStream.WriteAsync(bytes, ct);
        }
        finally
        {
            ctx.Response.Close();
        }
    }

    public async Task HandleSseAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.Add("Cache-Control", "no-cache");
        ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
        ctx.Response.Headers.Add("Connection", "keep-alive");
        ctx.Response.StatusCode = 200;

        var session = new SseSession(ctx.Response.OutputStream);
        _sessions[sessionId] = session;

        try
        {
            await session.SendAsync("endpoint", $"/mcp/message?sessionId={sessionId}", ct);
            while (!ct.IsCancellationRequested && !session.Closed)
            {
                await Task.Delay(15_000, ct);
                await session.SendAsync("ping", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            _sessions.TryRemove(sessionId, out _);
            session.Close();
        }
    }

    public async Task HandleMessageAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var sessionId = ctx.Request.QueryString["sessionId"] ?? "";
        _sessions.TryGetValue(sessionId, out var session);

        string body;
        using (var sr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
            body = await sr.ReadToEndAsync(ct);

        // ACK immediately per MCP spec
        ctx.Response.StatusCode = 202;
        ctx.Response.ContentLength64 = 0;
        ctx.Response.Close();

        if (session is null) return;

        try
        {
            var req    = JsonNode.Parse(body);
            var id     = req?["id"];
            var method = req?["method"]?.GetValue<string>() ?? "";
            var params_ = req?["params"];

            object result = method switch
            {
                "initialize"   => new
                {
                    protocolVersion = "2024-11-05",
                    capabilities    = new { tools = new { listChanged = false } },
                    serverInfo      = new { name = "vvvv-mcp-bridge", version = BridgeVersion.Current }
                },
                "tools/list"   => new { tools = ToolSchemas.LiveEditorTools },
                "tools/call"   => HandleToolCall(params_),
                "ping"         => (object)new { },
                _              => new { error = new { code = -32601, message = $"Method not found: {method}" } }
            };

            var response = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }, JsonOpts);
            await session.SendAsync("message", response, ct);
        }
        catch (Exception ex)
        {
            var err = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                error   = new { code = -32603, message = ex.Message }
            }, JsonOpts);
            await session.SendAsync("message", err, ct);
        }
    }

    private object HandleToolCall(JsonNode? params_)
    {
        var name   = params_?["name"]?.GetValue<string>() ?? "";
        var args   = params_?["arguments"]?.ToJsonString() ?? "{}";
        var result = _dispatch(name, args);
        return new { content = new[] { new { type = "text", text = result } } };
    }

    // ── SSE session ───────────────────────────────────────────────────────────

    private sealed class SseSession
    {
        private readonly Stream _stream;
        private readonly SemaphoreSlim _lock = new(1, 1);
        public bool Closed { get; private set; }

        public SseSession(Stream stream) => _stream = stream;

        public async Task SendAsync(string eventType, string data, CancellationToken ct)
        {
            if (Closed) return;
            await _lock.WaitAsync(ct);
            try
            {
                var bytes = Encoding.UTF8.GetBytes($"event: {eventType}\ndata: {data}\n\n");
                await _stream.WriteAsync(bytes, ct);
                await _stream.FlushAsync(ct);
            }
            catch { Closed = true; }
            finally { _lock.Release(); }
        }

        public void Close() { Closed = true; try { _stream.Close(); } catch { } }
    }
}

/// <summary>MCP tool schemas for the live-editor tools served by the bridge.</summary>
internal static class ToolSchemas
{
    private static object Tool(string name, string description, object properties, string[] required)
        => new { name, description, inputSchema = new { type = "object", properties, required } };

    private static object Prop(string type, string description, string? def = null)
        => def is null ? new { type, description } : (object)new { type, description, @default = def };

    public static readonly object[] LiveEditorTools =
    [
        Tool("check_bridge_connection",  "Check if vvvv bridge is running.", new { }, []),
        Tool("get_running_documents",    "List all .vl documents open in vvvv.", new { }, []),
        Tool("get_vvvv_errors",          "Get compilation errors from vvvv.", new { }, []),
        Tool("get_vvvv_state",           "Get vvvv runtime state.", new { }, []),
        Tool("get_vvvv_log",
            "Get recent log entries from the vvvv console.",
            new { limit    = Prop("integer", "Max entries", "50"),
                  severity = Prop("string",  "info | warning | error") }, []),
        Tool("get_open_tabs", "Get open tabs in the vvvv editor.", new { }, []),
        Tool("open_document_in_vvvv", "Open a .vl document in vvvv.",
            new { filePath = Prop("string", "Absolute path to the .vl file") }, ["filePath"]),
        Tool("close_document_in_vvvv", "Close a document in vvvv.",
            new { filePath = Prop("string", "Absolute path"),
                  save     = Prop("boolean", "Save before close") }, ["filePath"]),
        Tool("save_document_in_vvvv", "Save a document in vvvv. Use 'all' to save all.",
            new { filePath = Prop("string", "Absolute path or 'all'") }, ["filePath"]),
        Tool("reload_file_in_vvvv", "Force vvvv to reload a .vl file from disk.",
            new { filePath = Prop("string", "Absolute path") }, ["filePath"]),
        Tool("undo_in_vvvv", "Undo the last action.", new { }, []),
        Tool("redo_in_vvvv", "Redo the last action.", new { }, []),

        // ── Shared Core tools (same implementations as the external MCP server) ──
        Tool("build_patch",
            "Build a whole connected subgraph in ONE call: resolves nodes against the live vvvv registry (exact pins+types), adds NuGet deps, declares pins with correct visibility, auto-layouts, wires links (pin groups auto-index; endpoints accept 'key.Pin' or existing pin IDs from read_patch), saves, reloads, reports compile errors. THE primary way to create patch content.",
            new { spec = Prop("string", "JSON build spec: { filePath, nodes:[{key,name,category?,package?,kind?,bounds?,values?}], pads:[{key,type,value?,bounds?}], links:[{from,to}], verify?, open?, verbosity? }") },
            ["spec"]),
        Tool("search_nodes_live",
            "Search the LIVE node registry of this vvvv instance — exact pins, real types, only nodes actually placeable now.",
            new { query = Prop("string", "Node name, category, or keyword"),
                  category = Prop("string", "Optional category prefix filter"),
                  limit = Prop("integer", "Max results (default 20)") }, ["query"]),
        Tool("get_node_details_live",
            "Exact pin names, real pin types and defaults for a node from the LIVE registry.",
            new { name = Prop("string", "Node name (e.g. 'Box', 'LFO', 'Rotation (Successive)')"),
                  category = Prop("string", "Optional category hint to disambiguate") }, ["name"]),
        Tool("refresh_live_nodes", "Rebuild the live node snapshot (e.g. after installing a pack).", new { }, []),
        Tool("read_patch", "Parse a .vl patch file: nodes, pins, links, pads, dependencies.",
            new { filePath = Prop("string", "Absolute path to the .vl file") }, ["filePath"]),
        Tool("explain_patch", "Natural-language explanation of a .vl patch.",
            new { filePath = Prop("string", "Absolute path to the .vl file") }, ["filePath"]),
        Tool("list_patch_dependencies", "List NuGet dependencies of a .vl file.",
            new { filePath = Prop("string", "Absolute path to the .vl file") }, ["filePath"]),
    ];
}
