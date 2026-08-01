using System.Text.Json;
using System.Text.Json.Nodes;

namespace VvvvMcp;

/// <summary>
/// Handles the --setup subcommand: detects installed MCP clients and
/// writes / updates their configuration to point at this installation.
/// Works without any vvvv installation.
/// </summary>
public static class SetupCommand
{
    public static void Run()
    {
        Console.WriteLine("vvvv-mcp setup");
        Console.WriteLine("==============");
        Console.WriteLine();

        // Resolve what we have to offer
        var (serverExe, catalogPath, knowledgePath) = ResolveInstallPaths();

        Console.WriteLine($"Server   : {serverExe}");
        Console.WriteLine($"Catalog  : {catalogPath ?? "(not found — run `vvvv-mcp update-catalog`)"}");
        Console.WriteLine($"Knowledge: {knowledgePath ?? "(not found)"}");
        Console.WriteLine();

        // Build the environment variables block
        var env = new Dictionary<string, string>();
        if (catalogPath  is not null) env["VVVV_MCP_CATALOG"]   = catalogPath;
        if (knowledgePath is not null) env["VVVV_MCP_KNOWLEDGE"] = knowledgePath;

        var configured = new List<string>();

        // ── Claude Desktop ──────────────────────────────────────────────────
        var claudeDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Claude");
        if (Directory.Exists(claudeDir))
        {
            var configPath = Path.Combine(claudeDir, "claude_desktop_config.json");
            if (TryConfigureClient(configPath, "mcpServers", serverExe, env, isDotnetTool: true))
            {
                configured.Add("Claude Desktop");
                Console.WriteLine($"  [OK] Claude Desktop   -> {configPath}");
            }
        }

        // ── Cursor (global) ─────────────────────────────────────────────────
        var cursorDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cursor");
        if (Directory.Exists(cursorDir))
        {
            var configPath = Path.Combine(cursorDir, "mcp.json");
            if (TryConfigureClient(configPath, "mcpServers", serverExe, env, isDotnetTool: true))
            {
                configured.Add("Cursor (global)");
                Console.WriteLine($"  [OK] Cursor (global)  -> {configPath}");
            }
        }

        // ── VS Code user settings (global MCP list if supported) ───────────
        // VS Code stores MCP config in settings.json under "mcp.servers"
        var vsCodeSettingsPath = GetVsCodeUserSettingsPath();
        if (vsCodeSettingsPath is not null && File.Exists(vsCodeSettingsPath))
        {
            if (TryConfigureClient(vsCodeSettingsPath, "mcp.servers", serverExe, env, isDotnetTool: true))
            {
                configured.Add("VS Code (global)");
                Console.WriteLine($"  [OK] VS Code (global) -> {vsCodeSettingsPath}");
            }
        }

        Console.WriteLine();

        if (configured.Count == 0)
        {
            Console.WriteLine("No MCP clients detected automatically.");
            Console.WriteLine();
        }

        // ── Manual config snippet for everything else ───────────────────────
        Console.WriteLine("Add the following to any other MCP client config:");
        Console.WriteLine();
        PrintConfigSnippets(serverExe, env);

        Console.WriteLine();
        Console.WriteLine("Restart your MCP client to pick up the new configuration.");
    }

    // ── Path resolution ──────────────────────────────────────────────────────

    private static (string serverExe, string? catalogPath, string? knowledgePath) ResolveInstallPaths()
    {
        var baseDir = AppContext.BaseDirectory;

        // Catalog: bundled alongside binary, or fall back to repo layout
        var catalogCandidates = new[]
        {
            Path.Combine(baseDir, "vvvv_nodes_mcp.json"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "VVVVNodeAnalyzer", "output", "vvvv_nodes_mcp.json"))
        };
        var catalogPath = catalogCandidates.FirstOrDefault(File.Exists);

        // Knowledge: bundled as knowledge/ subfolder, or repo layout
        var knowledgeCandidates = new[]
        {
            Path.Combine(baseDir, "knowledge"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "knowledge"))
        };
        var knowledgePath = knowledgeCandidates.FirstOrDefault(d =>
            Directory.Exists(d) && Directory.GetFiles(d, "*.md").Length > 0);

        // Server executable: dotnet tool uses the command name directly
        // When installed as a global tool, clients just call `vvvv-mcp`
        // When run from a repo build, point at the DLL
        string serverExe;
        var dll = Path.Combine(baseDir, "VvvvMcp.dll");
        if (IsInstalledAsTool())
        {
            serverExe = "vvvv-mcp";
        }
        else if (File.Exists(dll))
        {
            serverExe = dll;
        }
        else
        {
            serverExe = Path.Combine(baseDir, "VvvvMcp.exe");
        }

        return (serverExe, catalogPath, knowledgePath);
    }

    private static bool IsInstalledAsTool()
    {
        // The tool store path contains ".dotnet/tools" or ".dotnet\tools"
        var baseDir = AppContext.BaseDirectory;
        return baseDir.Contains(Path.Combine(".dotnet", "tools"), StringComparison.OrdinalIgnoreCase)
            || baseDir.Contains(".dotnet/tools", StringComparison.OrdinalIgnoreCase);
    }

    // ── Config writing ───────────────────────────────────────────────────────

    /// <summary>
    /// Reads (or creates) a JSON config file and upserts the "vvvv" server entry
    /// under the given <paramref name="serversKey"/>.
    /// Returns true on success.
    /// </summary>
    private static bool TryConfigureClient(
        string configPath, string serversKey,
        string serverExe, Dictionary<string, string> env,
        bool isDotnetTool)
    {
        try
        {
            JsonNode root;
            if (File.Exists(configPath))
            {
                var text = File.ReadAllText(configPath);
                root = JsonNode.Parse(text) ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            // Navigate / create the servers object (supports dot-notation like "mcp.servers")
            var parts  = serversKey.Split('.');
            var parent = root;
            foreach (var part in parts[..^1])
            {
                if (parent[part] is not JsonObject)
                    parent[part] = new JsonObject();
                parent = parent[part]!;
            }
            var key = parts[^1];
            if (parent[key] is not JsonObject)
                parent[key] = new JsonObject();
            var servers = (JsonObject)parent[key]!;

            // Build the server entry
            var entry = BuildServerEntry(serverExe, env, isDotnetTool);
            servers["vvvv"] = entry;

            var options = new JsonSerializerOptions { WriteIndented = true };
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, root.ToJsonString(options));
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [WARN] Could not configure {configPath}: {ex.Message}");
            return false;
        }
    }

    private static JsonObject BuildServerEntry(
        string serverExe, Dictionary<string, string> env, bool isDotnetTool)
    {
        var envNode = new JsonObject();
        foreach (var kv in env)
            envNode[kv.Key] = kv.Value;

        if (isDotnetTool && serverExe == "vvvv-mcp")
        {
            // dotnet global tool — clients call it by command name
            return new JsonObject
            {
                ["type"]    = "stdio",
                ["command"] = "vvvv-mcp",
                ["env"]     = envNode
            };
        }
        else
        {
            // DLL run via dotnet — used when running from a repo clone
            return new JsonObject
            {
                ["type"]    = "stdio",
                ["command"] = "dotnet",
                ["args"]    = new JsonArray(serverExe),
                ["env"]     = envNode
            };
        }
    }

    // ── VS Code settings path detection ──────────────────────────────────────

    private static string? GetVsCodeUserSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var candidates = new[]
        {
            // Windows
            Path.Combine(appData, "Code", "User", "settings.json"),
            Path.Combine(appData, "Code - Insiders", "User", "settings.json"),
            // macOS
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "Code", "User", "settings.json"),
            // Linux
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "Code", "User", "settings.json"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    // ── Printed snippets ─────────────────────────────────────────────────────

    private static void PrintConfigSnippets(string serverExe, Dictionary<string, string> env)
    {
        var envJson = string.Join(",\n", env.Select(kv =>
            $"        \"{kv.Key}\": \"{kv.Value.Replace("\\", "\\\\")}\""));

        string cmdLines;
        if (serverExe == "vvvv-mcp")
        {
            cmdLines = "      \"command\": \"vvvv-mcp\"";
        }
        else
        {
            var escaped = serverExe.Replace("\\", "\\\\");
            cmdLines = "      \"command\": \"dotnet\",\n      \"args\": [\"" + escaped + "\"]";
        }

        static string McpBlock(string serverKey, string cmdLines, string envJson) =>
            "{\n" +
            "  \"" + serverKey + "\": {\n" +
            "    \"vvvv\": {\n" +
            "      \"type\": \"stdio\",\n" +
            cmdLines + ",\n" +
            "      \"env\": {\n" +
            envJson + "\n" +
            "      }\n" +
            "    }\n" +
            "  }\n" +
            "}";

        Console.WriteLine("  Claude Desktop  (%APPDATA%\\Claude\\claude_desktop_config.json)");
        Console.WriteLine("  Cursor          (~/.cursor/mcp.json)");
        Console.WriteLine();
        Console.WriteLine(McpBlock("mcpServers", cmdLines, envJson));
        Console.WriteLine();
        Console.WriteLine("  VS Code / Kilo  (.vscode/mcp.json  or  user settings.json)");
        Console.WriteLine();
        Console.WriteLine(McpBlock("servers", cmdLines, envJson));
    }
}
