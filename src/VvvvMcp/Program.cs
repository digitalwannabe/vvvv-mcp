using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using VvvvMcp;
using VvvvMcp.Core.Services;

// ── Sub-commands (run before MCP server starts) ───────────────────────────────

if (args.Length > 0)
{
    switch (args[0].ToLowerInvariant())
    {
        case "--setup":
        case "setup":
            SetupCommand.Run();
            return;

        case "--version":
        case "version":
            Console.WriteLine("vvvv-mcp 0.2.0");
            return;

        case "--help":
        case "help":
        case "-h":
            PrintHelp();
            return;
    }
}

// ── MCP server ────────────────────────────────────────────────────────────────

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton<NodeCatalogService>();
builder.Services.AddSingleton<PatchReaderService>();
builder.Services.AddSingleton<PatchExplainerService>();
builder.Services.AddSingleton<PatchWriterService>();
builder.Services.AddSingleton<PluginGeneratorService>();
builder.Services.AddSingleton<ShaderGeneratorService>();
builder.Services.AddSingleton<KnowledgeService>();
builder.Services.AddSingleton<BridgeClientService>();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name    = "vvvv-mcp",
            Version = "0.2.0"
        };
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly()
    .WithPromptsFromAssembly();

var host = builder.Build();

var catalogService  = host.Services.GetRequiredService<NodeCatalogService>();
var knowledgeService = host.Services.GetRequiredService<KnowledgeService>();
var logger          = host.Services.GetRequiredService<ILogger<Program>>();

// --- Node catalog ---
// Priority: VVVV_MCP_CATALOG env var → bundled alongside binary → repo dev layout
var catalogPath = Environment.GetEnvironmentVariable("VVVV_MCP_CATALOG");
if (catalogPath is null || !File.Exists(catalogPath))
{
    var candidates = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "vvvv_nodes_mcp.json"),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "VVVVNodeAnalyzer", "output", "vvvv_nodes_mcp.json")),
    };
    catalogPath = candidates.FirstOrDefault(File.Exists);
}

if (catalogPath is not null)
{
    try
    {
        await catalogService.LoadAsync(catalogPath);
        logger.LogInformation("Node catalog loaded from {Path}", catalogPath);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to load node catalog from {Path}. Node search will be unavailable.", catalogPath);
    }
}
else
{
    logger.LogWarning("Node catalog not found. Run `vvvv-mcp --setup` or set VVVV_MCP_CATALOG. Node search will be unavailable.");
}

// --- Knowledge base ---
// Priority: VVVV_MCP_KNOWLEDGE env var → knowledge/ bundled alongside binary → repo dev layout
var knowledgePath = Environment.GetEnvironmentVariable("VVVV_MCP_KNOWLEDGE");
if (knowledgePath is null || !Directory.Exists(knowledgePath))
{
    var candidates = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "knowledge"),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "knowledge")),
    };
    knowledgePath = candidates.FirstOrDefault(d =>
        Directory.Exists(d) && Directory.GetFiles(d, "*.md").Length > 0);
}

if (knowledgePath is not null)
{
    try
    {
        await knowledgeService.LoadAsync(knowledgePath);
        logger.LogInformation("Knowledge base loaded from {Path}", knowledgePath);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to load knowledge base from {Path}.", knowledgePath);
    }
}
else
{
    logger.LogWarning("Knowledge base not found. Run `vvvv-mcp --setup` or set VVVV_MCP_KNOWLEDGE.");
}

// --- Bridge client (connects to running vvvv if VL.MCP.Bridge is loaded) ---
var bridgeClient = host.Services.GetRequiredService<BridgeClientService>();
var bridgeAvailable = await bridgeClient.CheckAvailabilityAsync();
if (bridgeAvailable)
{
    logger.LogInformation("vvvv bridge detected at localhost (live tools enabled)");
}
else
{
    logger.LogInformation("No vvvv bridge detected (live tools will report 'not connected' until VL.MCP.Bridge.HDE.vl is loaded in vvvv)");
}

await host.RunAsync();

// ── Help text ─────────────────────────────────────────────────────────────────

static void PrintHelp()
{
    Console.WriteLine("""
        vvvv-mcp — MCP server for vvvv gamma

        Usage:
          vvvv-mcp               Start MCP server (stdio, for use by MCP clients)
          vvvv-mcp --setup       Configure MCP clients (Claude Desktop, Cursor, VS Code)
          vvvv-mcp --version     Print version
          vvvv-mcp --help        Show this help

        Environment variables:
          VVVV_MCP_CATALOG       Path to vvvv_nodes_mcp.json (auto-detected if not set)
          VVVV_MCP_KNOWLEDGE     Path to knowledge/ directory (auto-detected if not set)

        Install (requires .NET 8 SDK):
          dotnet tool install -g vvvv-mcp

        Update catalog (downloads all vvvv packages from NuGet, no vvvv install needed):
          ./scripts/update-catalog.ps1

        Docs: https://github.com/digitalwannabe/mcp-gamma-server
        """);
}
