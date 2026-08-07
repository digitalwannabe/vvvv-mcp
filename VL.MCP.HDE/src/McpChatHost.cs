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
    /// <summary>Human-readable status for the placeholder page.</summary>
    public string  Status    => _lastError ?? (_ready ? "ready" : "setting up…");
    public string  ChatUrl   => $"http://localhost:{_chatPort}";

    public (bool IsReady, bool IsStarting, string? LastError, string ChatUrl) Update(
        bool enabled, int chatPort = 7125, int bridgePort = 7123)
    {
        _chatPort   = chatPort;
        _bridgePort = bridgePort;

        if (enabled && !_currentEnabled)
        {
            _currentEnabled = true;
            _lastError      = null;
            // Fast path: process (started or adopted) still alive → nothing to do,
            // the window just re-opens on the existing server.
            if (_process is { HasExited: false } && _ready)
            {
                _startupTask = Task.CompletedTask;
            }
            else
            {
                _cts         = new CancellationTokenSource();
                _ready       = false;
                _startupTask = Task.Run(() => StartAsync(_cts.Token));
            }
        }
        else if (!enabled && _currentEnabled)
        {
            _currentEnabled = false;
            // IMPORTANT: do NOT cancel _cts or the startup task here.
            // The "Open Chat" signal is a one-frame bang (HoldLatest.On Data), so
            // enabled drops to false the very next frame — cancelling would abort
            // the Open WebUI startup mid-flight (this was the bug that left the
            // placeholder polling forever). The server, once started, lives until
            // vvvv exits (Dispose). Only the window toggles with the signal.
        }

        if (_startupTask?.IsFaulted == true && _lastError is null)
            _lastError = _startupTask.Exception?.GetBaseException().Message ?? "Unknown error";

        return (_ready, enabled && _startupTask is { IsCompleted: false },
                _lastError, $"http://localhost:{chatPort}");
    }

    // ── Startup ───────────────────────────────────────────────────────────────

    private async Task StartAsync(CancellationToken ct)
    {
        // Machine-wide named mutex: serializes Open WebUI startups across concurrent
        // Alt+C presses AND HDE hot-recompiles (a new assembly = fresh instance lock,
        // but the named mutex survives). After a previous start completes, the next
        // caller ADOPTS the now-running instance instead of spawning a duplicate.
        using var mutex = new Mutex(initiallyOwned: false, @"Global\vvvv-mcp-chat-start");
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromMinutes(6)); }
            catch (AbandonedMutexException) { acquired = true; } // previous holder died — we own it now

            if (!acquired)
            {
                _lastError = "Timed out waiting for the Open WebUI startup lock.";
                return;
            }

            // Adopt: a healthy Open WebUI may already be listening (previous start,
            // orphan from an earlier vvvv run, or a start that just completed).
            if (await TryAdoptExistingAsync(ct))
                return;

            await StartFreshAsync(ct);
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }

    private async Task StartFreshAsync(CancellationToken ct)
    {
        var uv = FindOnPath("uv.exe") ?? FindOnPath("uv");
        if (uv is null)
        {
            _lastError = "uv not found. Install: powershell -c \"irm https://astral.sh/uv/install.ps1 | iex\"";
            return;
        }

        // 2. Port occupied by something that is NOT a healthy Open WebUI.
        //    Only kill it if it looks like a stale open-webui/uv/python leftover —
        //    never nuke foreign processes.
        if (!KillStaleOpenWebUiOnPort(_chatPort))
        {
            _lastError = $"Port {_chatPort} is occupied by a non-Open-WebUI process. Free it or set VVVV_MCP_CHAT_PORT.";
            return;
        }
        await Task.Delay(1500, ct); // give OS time to release the socket

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "vvvv-mcp", "openwebui-data");
        Directory.CreateDirectory(dataDir);

        // Inject an aiodns stub via PYTHONPATH so aiohttp uses ThreadedResolver instead of
        // AsyncResolver (aiodns/c-ares). c-ares reads DNS server addresses once at process
        // initialization from the Windows registry and never refreshes them — after a VPN
        // connect/disconnect cycle the captured addresses become stale and external DNS stops
        // resolving. ThreadedResolver calls getaddrinfo() per-request, which always reads the
        // current system DNS and is immune to this problem. The stub just raises ImportError;
        // aiohttp's "try: import aiodns" check then falls back to ThreadedResolver automatically,
        // identical to the state where aiodns is not installed at all.
        var stubDir = Path.Combine(dataDir, "python-overrides");
        Directory.CreateDirectory(stubDir);
        File.WriteAllText(Path.Combine(stubDir, "aiodns.py"),
            "# vvvv-mcp stub: prevents aiodns/c-ares from caching stale DNS after VPN changes.\n" +
            "# aiohttp falls back to ThreadedResolver (getaddrinfo per request) which is immune.\n" +
            "raise ImportError(\n" +
            "    'aiodns disabled by vvvv-mcp to prevent stale DNS after VPN changes; '\n" +
            "    'aiohttp will use ThreadedResolver (system getaddrinfo) instead.'\n" +
            ")\n");

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

        // Prepend our stub directory to PYTHONPATH so the aiodns stub shadows the real package.
        psi.Environment.TryGetValue("PYTHONPATH", out var existingPythonPath);
        psi.Environment["PYTHONPATH"] = string.IsNullOrEmpty(existingPythonPath)
            ? stubDir
            : $"{stubDir};{existingPythonPath}";

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => ForwardOpenWebUiLine(e.Data, isError: false);
        _process.ErrorDataReceived  += (_, e) => ForwardOpenWebUiLine(e.Data, isError: true);
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
        {
            // Non-fatal: server is up even if MCP registration fails.
            try { await RegisterMcpServerAsync(ct); }
            catch (Exception ex) { Console.WriteLine($"[vvvv-mcp] MCP registration failed (non-fatal): {ex.Message}"); }
        }
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

    // ── Open WebUI output handling ────────────────────────────────────────────
    // Every line goes into a rolling buffer (readable via /api/chat/log for debugging).
    // Only error-ish lines are forwarded to the vvvv console — OWUI's startup logging
    // (alembic migrations, CORS notice, embedding-model load report) is benign but noisy.

    private readonly object _logGate = new();
    private readonly Queue<string> _owuiLog = new();
    private const int OwuiLogCapacity = 500;

    /// <summary>Rolling buffer of all Open WebUI output lines (for /api/chat/log).</summary>
    public string[] GetOpenWebUiLog()
    {
        lock (_logGate) return _owuiLog.ToArray();
    }

    private void ForwardOpenWebUiLine(string? line, bool isError)
    {
        if (line is null) return;

        lock (_logGate)
        {
            _owuiLog.Enqueue(line);
            while (_owuiLog.Count > OwuiLogCapacity) _owuiLog.Dequeue();
        }

        if (isError || LooksImportant(line))
            Console.WriteLine($"[OpenWebUI] {line}");
    }

    private static bool LooksImportant(string line)
    {
        return line.Contains("Traceback", StringComparison.Ordinal)
            || line.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
            || line.Contains("CRITICAL", StringComparison.OrdinalIgnoreCase)
            || line.Contains("FATAL", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Exception", StringComparison.Ordinal)
            || line.Contains("Failed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("bind", StringComparison.OrdinalIgnoreCase)
            || line.Contains("error", StringComparison.Ordinal);
    }

    // ── Port + process cleanup ────────────────────────────────────────────────

    /// <summary>
    /// If a healthy Open WebUI already answers on the chat port, adopt it instead
    /// of starting a second instance. Returns true when adopted.
    /// </summary>
    private async Task<bool> TryAdoptExistingAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await Http.GetAsync($"http://127.0.0.1:{_chatPort}/", ct);
            if (!resp.IsSuccessStatusCode) return false;
            var body = await resp.Content.ReadAsStringAsync(ct);
            var looksLikeOpenWebUi =
                body.Contains("Open WebUI", StringComparison.OrdinalIgnoreCase) ||
                body.Contains("open-webui", StringComparison.OrdinalIgnoreCase) ||
                body.Contains("openwebui",   StringComparison.OrdinalIgnoreCase);
            if (!looksLikeOpenWebUi) return false;

            var pid = FindPidOnPort(_chatPort);
            if (pid > 0)
            {
                try
                {
                    _process = Process.GetProcessById(pid);
                    _process.EnableRaisingEvents = true;
                    _process.Exited += (_, _) =>
                    {
                        if (_currentEnabled) _lastError = "Open WebUI exited unexpectedly.";
                        _ready = false;
                    };
                }
                catch { }
            }

            Console.WriteLine($"[vvvv-mcp] Adopting already-running Open WebUI on port {_chatPort} (PID {pid})");
            _ready = true;
            if (!_mcpRegistered)
            {
                // Non-fatal: a failed MCP registration must not undo a successful adopt.
                try { await RegisterMcpServerAsync(ct); }
                catch (Exception ex) { Console.WriteLine($"[vvvv-mcp] MCP registration failed (non-fatal): {ex.Message}"); }
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Kills the process listening on the port ONLY if it looks like a stale
    /// Open WebUI leftover (python/uv process). Returns false (and kills nothing)
    /// when a foreign process owns the port or the port is free.
    /// </summary>
    private bool KillStaleOpenWebUiOnPort(int port)
    {
        var pid = FindPidOnPort(port);
        if (pid <= 0) return true; // port free

        try
        {
            var proc = Process.GetProcessById(pid);
            var name = proc.ProcessName.ToLowerInvariant();
            var looksStale = name.Contains("python") || name.Contains("uv");
            if (!looksStale)
            {
                Console.WriteLine($"[vvvv-mcp] Port {port} held by foreign process {proc.ProcessName} (PID {pid}) — not killing it.");
                return false;
            }
            Console.WriteLine($"[vvvv-mcp] Killing stale Open WebUI process {proc.ProcessName} (PID {pid}) on port {port}");
            proc.Kill(entireProcessTree: true);
            return true;
        }
        catch { return true; } // process already gone
    }

    private static int FindPidOnPort(int port)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo("netstat", "-ano")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                CreateNoWindow         = true
            });
            if (p is null) return -1;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);

            foreach (var line in output.Split('\n'))
            {
                if (!line.Contains($":{port} ") && !line.Contains($":{port}\t")) continue;
                if (!line.Contains("LISTENING")) continue;
                var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5) continue;
                if (int.TryParse(parts[^1], out var pid) && pid > 0)
                    return pid;
            }
        }
        catch { }
        return -1;
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
