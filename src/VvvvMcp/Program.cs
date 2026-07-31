using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using VvvvMcp.Core.Services;

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

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "vvvv-mcp",
            Version = "0.2.0"
        };
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly()
    .WithPromptsFromAssembly();

var host = builder.Build();

var catalogService = host.Services.GetRequiredService<NodeCatalogService>();
var knowledgeService = host.Services.GetRequiredService<KnowledgeService>();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

// --- Node catalog ---
var catalogPath = Environment.GetEnvironmentVariable("VVVV_MCP_CATALOG");
if (catalogPath is null || !File.Exists(catalogPath))
{
    var candidates = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "vvvv_nodes_mcp.json"),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "VVVVNodeAnalyzer", "vvvv_nodes_mcp.json"),
    };
    catalogPath = candidates.FirstOrDefault(File.Exists);
}

if (catalogPath is not null)
{
    try
    {
        await catalogService.LoadAsync(catalogPath);
        logger.LogInformation("Node catalog loaded successfully from {Path}", catalogPath);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to load node catalog from {Path}. Node search will be unavailable.", catalogPath);
    }
}
else
{
    logger.LogWarning("Node catalog not found. Set VVVV_MCP_CATALOG environment variable. Node search will be unavailable.");
}

// --- Knowledge base ---
var knowledgePath = Environment.GetEnvironmentVariable("VVVV_MCP_KNOWLEDGE");
if (knowledgePath is null || !Directory.Exists(knowledgePath))
{
    var candidates = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "knowledge"),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "knowledge"),
    };
    knowledgePath = candidates.FirstOrDefault(Directory.Exists);
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
    logger.LogWarning("Knowledge base directory not found. Set VVVV_MCP_KNOWLEDGE environment variable.");
}

await host.RunAsync();
