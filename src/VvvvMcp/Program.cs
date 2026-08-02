using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.Reflection;
using VvvvMcp;
using VvvvMcp.Core.Services;

// ── Sub-commands (run before MCP server starts) ───────────────────────────────

var appVersion = GetAppVersion();

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
            Console.WriteLine($"vvvv-mcp {appVersion}");
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
builder.Services.AddSingleton<TemplateService>();
builder.Services.AddSingleton<PluginGeneratorService>();
builder.Services.AddSingleton<ShaderGeneratorService>();
builder.Services.AddSingleton<KnowledgeService>();
builder.Services.AddSingleton<SearchIndexService>();
builder.Services.AddSingleton<BridgeClientService>();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name    = "vvvv-mcp",
            Version = appVersion
        };
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly()
    .WithPromptsFromAssembly();

var host = builder.Build();

var catalogService   = host.Services.GetRequiredService<NodeCatalogService>();
var knowledgeService = host.Services.GetRequiredService<KnowledgeService>();
var templateService  = host.Services.GetRequiredService<TemplateService>();
var searchIndex      = host.Services.GetRequiredService<SearchIndexService>();
var logger           = host.Services.GetRequiredService<ILogger<Program>>();

// --- Node catalog ---
// Priority: bundled alongside binary → repo dev layout
var catalogCandidates = new[]
{
    Path.Combine(AppContext.BaseDirectory, "vvvv_nodes_mcp.json"),
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "VVVVNodeAnalyzer", "output", "vvvv_nodes_mcp.json")),
};
var catalogPath = catalogCandidates.FirstOrDefault(File.Exists);

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
    logger.LogWarning("Node catalog not found. Run `vvvv-mcp --setup`. Node search will be unavailable.");
}

// --- Knowledge base + templates ---
// Priority: knowledge/ bundled alongside binary → repo dev layout
var knowledgeCandidates = new[]
{
    Path.Combine(AppContext.BaseDirectory, "knowledge"),
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "knowledge")),
};
var knowledgePath = knowledgeCandidates.FirstOrDefault(d =>
    Directory.Exists(d) && Directory.GetFiles(d, "*.md").Length > 0);

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

    try
    {
        await templateService.LoadAsync(knowledgePath);
        logger.LogInformation("Templates loaded ({Count} files)", templateService.ListTemplates().Count);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to load templates from {Path}/templates.", knowledgePath);
    }
}
else
{
    logger.LogWarning("Knowledge base not found. Run `vvvv-mcp --setup`.");
}

// --- SQLite FTS5 search index ---
// Covers knowledge docs, node catalog, and practical data (help patches, forum).
// The DB lives next to the knowledge/ dir so it is always co-located with its sources.
// It is gitignored and fully regenerated at every startup (sub-second for existing data).
if (knowledgePath is not null)
{
    try
    {
        var dbPath = Path.Combine(knowledgePath, "vvvv-search.db");
        await searchIndex.InitializeAsync(dbPath);

        // Index knowledge files
        if (knowledgeService.IsLoaded)
        {
            await searchIndex.RebuildKnowledgeIndexAsync(knowledgeService.GetAllFiles());
            knowledgeService.SetSearchIndex(searchIndex);
        }

        // Index node catalog
        if (catalogService.IsLoaded)
        {
            await searchIndex.RebuildNodeIndexAsync(catalogService.GetAllNodes());
            catalogService.SetSearchIndex(searchIndex);
        }

        // Index practical data (help patches, forum, etc.) from generated JSON/MD files
        await searchIndex.RebuildPracticalIndexAsync(knowledgePath);

        var (kCount, nCount, pCount) = searchIndex.GetStats();
        logger.LogInformation(
            "Search index ready — knowledge: {K}, nodes: {N}, practical: {P}",
            kCount, nCount, pCount);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to initialize search index. Falling back to in-memory search.");
    }
}

// --- Bridge client (connects to running vvvv if VL.MCP.Bridge is loaded) ---
var bridgeClient    = host.Services.GetRequiredService<BridgeClientService>();
var bridgeAvailable = await bridgeClient.CheckAvailabilityAsync();
if (bridgeAvailable)
    logger.LogInformation("vvvv bridge detected at localhost (live tools enabled)");
else
    logger.LogInformation("No vvvv bridge detected (live tools will report 'not connected' until VL.MCP.Bridge.HDE.vl is loaded in vvvv)");

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

        Install (requires .NET 8 SDK):
          dotnet tool install -g vvvv-mcp

        Update catalog (downloads all vvvv packages from NuGet, no vvvv install needed):
          ./scripts/update-catalog.ps1

        Rebuild search index (after running index-help-patches.ps1 / scrape-forum.ps1):
          Just restart the MCP server — it rebuilds vvvv-search.db on every startup.

        Docs: https://github.com/digitalwannabe/mcp-gamma-server
        """);
}

static string GetAppVersion()
{
    var assembly = typeof(Program).Assembly;

    var informational = assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion;

    if (!string.IsNullOrWhiteSpace(informational))
        return informational.Split('+')[0];

    var fileVersion = assembly
        .GetCustomAttribute<AssemblyFileVersionAttribute>()
        ?.Version;

    if (Version.TryParse(fileVersion, out var parsed) && parsed.Revision == 0)
        return $"{parsed.Major}.{parsed.Minor}.{parsed.Build}";

    return fileVersion ?? "0.0.0";
}
