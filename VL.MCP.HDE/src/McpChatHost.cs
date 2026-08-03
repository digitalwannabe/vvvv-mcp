using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Net.Sockets;

namespace VL.MCP;

/// <summary>
/// Manages the Open WebUI child process for the Chat mode.
/// Uses uv (https://docs.astral.sh/uv/) to run Open WebUI without a permanent Python install.
/// Install uv: powershell -c "irm https://astral.sh/uv/install.ps1 | iex"
/// </summary>
internal class McpChatHost : IDisposable
{
    private Process?    _process;
    private bool        _ready;
    private bool        _mcpRegistered;
    private string?     _lastError;
    private Task?       _startupTask;
    private CancellationTokenSource? _cts;
    private bool        _currentEnabled;
    private int         _chatPort;
    private int         _bridgePort;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public bool    IsReady   => _ready;
    public string? LastError => _lastError;

    public (bool IsReady, bool IsStarting, string? LastError, string ChatUrl) Update(
        bool enabled, int chatPort = 7125, int bridgePort = 7123)
    {
        _chatPort   = chatPort;
        _bridgePort = bridgePort;

        if (enabled && !_currentEnabled)
        {
            _currentEnabled = true;
            _lastError      = null;
            _ready          = false;
            _cts            = new CancellationTokenSource();
            _startupTask    = Task.Run(() => StartAsync(_cts.Token));
        }
        else if (!enabled && _currentEnabled)
        {
            _currentEnabled = false;
            _cts?.Cancel();
            KillProcess();
            _ready       = false;
            _startupTask = null;
        }

        if (_startupTask?.IsFaulted == true && _lastError is null)
            _lastError = _startupTask.Exception?.GetBaseException().Message ?? "Unknown error";

        return (_ready, enabled && _startupTask is { IsCompleted: false },
                _lastError, $"http://localhost:{chatPort}");
    }

    // ── Startup ───────────────────────────────────────────────────────────────

    private async Task StartAsync(CancellationToken ct)
    {
        var uv = FindOnPath("uv.exe") ?? FindOnPath("uv");
        if (uv is null)
        {
            _lastError = "uv not found. Install: powershell -c \"irm https://astral.sh/uv/install.ps1 | iex\"";
            return;
        }

        // Kill any orphaned Open WebUI process still holding the port from a previous session
        KillProcessOnPort(_chatPort);
        await Task.Delay(1500, ct); // give OS time to release the socket

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "vvvv-mcp", "openwebui-data");
        Directory.CreateDirectory(dataDir);

        // uvx installs open-webui once into a dedicated tool env and reuses it —
        // much faster on subsequent starts, no re-resolve, no re-download.
        var psi = new ProcessStartInfo(uv, $"run --with open-webui open-webui serve --port {_chatPort}")
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            WorkingDirectory       = dataDir   // open-webui writes ./data relative to cwd
        };
        psi.Environment["WEBUI_AUTH"]      = "False";
        psi.Environment["ENABLE_SIGNUP"]   = "False";
        psi.Environment["OLLAMA_BASE_URL"] = "http://localhost:11434";
        psi.Environment["DATA_DIR"]        = dataDir;   // explicit: always same location

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine($"[OpenWebUI] {e.Data}"); };
        _process.ErrorDataReceived  += (_, e) => { if (e.Data is not null) Console.Error.WriteLine($"[OpenWebUI] {e.Data}"); };
        _process.Exited += (_, _) =>
        {
            if (_currentEnabled) _lastError = "Open WebUI exited unexpectedly.";
            _ready = false;
        };
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        // Poll TCP port until Open WebUI accepts connections (up to 5 min first run)
        var deadline = DateTime.UtcNow.AddMinutes(5);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(2000, ct);
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync("127.0.0.1", _chatPort, ct);
                _ready = true;
                break;
            }
            catch { }
        }

        if (!_ready)
            _lastError = $"Open WebUI did not start within 5 min. Check console for [OpenWebUI] messages.";
        else if (!_mcpRegistered)
            await RegisterMcpServerAsync(ct);
    }

    private async Task RegisterMcpServerAsync(CancellationToken ct)
    {
        try
        {
            var baseUrl  = $"http://localhost:{_chatPort}";
            var mcpUrl   = $"http://localhost:{_bridgePort}/mcp";

            // 1. Get auth token — WEBUI_AUTH=False allows empty credentials
            using var authReq  = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/v1/auths/signin")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { email = "", password = "" }),
                    Encoding.UTF8, "application/json")
            };
            var authResp = await Http.SendAsync(authReq, ct);
            if (!authResp.IsSuccessStatusCode) return;

            using var authDoc = JsonDocument.Parse(await authResp.Content.ReadAsStringAsync(ct));
            var token = authDoc.RootElement.GetProperty("token").GetString();
            if (token is null) return;

            // 2. Get current tool server configs
            using var getReq = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/v1/configs/tool_servers");
            getReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var getResp = await Http.SendAsync(getReq, ct);

            // Check if our server is already registered
            if (getResp.IsSuccessStatusCode)
            {
                var existing = await getResp.Content.ReadAsStringAsync(ct);
                if (existing.Contains("vvvv-mcp") || existing.Contains($"{_bridgePort}/mcp"))
                { _mcpRegistered = true; return; }
            }

            // 3. POST our server config — type "mcp" for native MCP/SSE, path="" for direct transport
            var payload = new
            {
                TOOL_SERVER_CONNECTIONS = new[]
                {
                    new
                    {
                        type      = "mcp",
                        url       = mcpUrl,
                        path      = "",
                        auth_type = "none",
                        key       = (string?)null,
                        config    = new { enable = true },
                        info      = new { id = "vvvv-mcp", name = "vvvv-mcp",
                                          description = "vvvv live editor and patch tools" }
                    }
                }
            };

            using var postReq = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/v1/configs/tool_servers")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            postReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var postResp = await Http.SendAsync(postReq, ct);
            _mcpRegistered = postResp.IsSuccessStatusCode;
        }
        catch { /* non-fatal — configure manually via Settings → Connections if this fails */ }
    }

    // ── PATH lookup ───────────────────────────────────────────────────────────

    private static string? FindOnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(';'))
        {
            var full = Path.Combine(dir.Trim(), exe);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    // ── Port + process cleanup ────────────────────────────────────────────────

    private static void KillProcessOnPort(int port)
    {
        // Use netstat to find the PID holding the port and kill it
        try
        {
            var p = Process.Start(new ProcessStartInfo("netstat", "-ano")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                CreateNoWindow         = true
            });
            if (p is null) return;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);

            foreach (var line in output.Split('\n'))
            {
                if (!line.Contains($":{port} ") && !line.Contains($":{port}\t")) continue;
                if (!line.Contains("LISTENING") && !line.Contains("ESTABLISHED")) continue;
                var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5) continue;
                if (!int.TryParse(parts[^1], out var pid) || pid <= 0) continue;
                try
                {
                    var victim = Process.GetProcessById(pid);
                    Console.WriteLine($"[vvvv-mcp] Killing orphaned process {victim.ProcessName} (PID {pid}) on port {port}");
                    victim.Kill(entireProcessTree: true);
                }
                catch { }
            }
        }
        catch { }
    }

    private void KillProcess()
    {
        if (_process is null) return;
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { }
        try { _process.WaitForExit(3000); } catch { } // wait so OS releases the socket
        try { _process.Dispose(); } catch { }
        _process = null;
    }

    public void Dispose() { _cts?.Cancel(); KillProcess(); }
}
