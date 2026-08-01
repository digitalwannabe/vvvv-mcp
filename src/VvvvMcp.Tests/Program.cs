using VvvvMcp.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

Console.WriteLine("=== vvvv-mcp Smoke Test ===");
Console.WriteLine();

// Test 1: NodeCatalogService
Console.WriteLine("[1] Testing NodeCatalogService...");
var catalogLogger = NullLogger<NodeCatalogService>.Instance;
var catalog = new NodeCatalogService(catalogLogger);

var catalogPath = args.Length > 0 ? args[0] : @"x:\_dev\vvvv-mcp\VVVVNodeAnalyzer\output\vvvv_nodes_mcp.json";
if (!File.Exists(catalogPath))
{
    Console.WriteLine($"  ERROR: Catalog not found at {catalogPath}");
    return 1;
}

await catalog.LoadAsync(catalogPath);
var stats = catalog.GetStats();
Console.WriteLine($"  OK Loaded {stats.TotalNodes} nodes from {stats.TotalPackages} packages in {stats.TotalCategories} categories");

// Search test
var searchResults = catalog.Search("Transform", limit: 5);
Console.WriteLine($"  OK Search 'Transform': {searchResults.Count} results");
foreach (var r in searchResults)
{
    Console.WriteLine($"    - {r.Node.Name} ({r.Node.Package}) score={r.Score:F1}");
}

var boxResults = catalog.Search("Box", limit: 3);
Console.WriteLine($"  OK Search 'Box': {boxResults.Count} results");
foreach (var r in boxResults)
{
    var summary = r.Node.Summary.Length > 60 ? r.Node.Summary[..60] + "..." : r.Node.Summary;
    Console.WriteLine($"    - {r.Node.Name} ({r.Node.Package}) [{summary}]");
}

// GetByName test
var transformNodes = catalog.GetByName("TransformSRT");
Console.WriteLine($"  OK GetByName 'TransformSRT': {transformNodes.Count} match(es)");
if (transformNodes.Count > 0)
{
    Console.WriteLine($"    Summary: {transformNodes[0].Summary}");
}

// Categories test
var categories = catalog.GetCategories("3D");
Console.WriteLine($"  OK Categories starting with '3D': {categories.Count}");

Console.WriteLine();

// Test 2: PatchReaderService
Console.WriteLine("[2] Testing PatchReaderService...");
var patchLogger = NullLogger<PatchReaderService>.Instance;
var patchReader = new PatchReaderService(patchLogger);

var patchPath = @"x:\_dev\vvvv-mcp\VVVVNodeAnalyzer\simple patch.vl";
if (File.Exists(patchPath))
{
    var patch = patchReader.ReadPatch(patchPath);
    Console.WriteLine($"  OK Parsed '{Path.GetFileName(patchPath)}':");
    Console.WriteLine($"    - Document ID: {patch.DocumentId}");
    Console.WriteLine($"    - Language Version: {patch.LanguageVersion}");
    Console.WriteLine($"    - Dependencies: {patch.Dependencies.Count}");
    Console.WriteLine($"    - Nodes: {patch.AllNodes.Count}");
    Console.WriteLine($"    - Pads: {patch.AllPads.Count}");
    Console.WriteLine($"    - Links: {patch.Links.Count}");
    
    foreach (var node in patch.AllNodes.Where(n => n.Reference.NodeName != null))
    {
        var visiblePins = node.Pins.Where(p => !p.IsHidden).Count();
        Console.WriteLine($"    - Node: {node.Reference.NodeName} ({node.Reference.LastCategoryFullName}) - {visiblePins} visible pins");
    }
}
else
{
    Console.WriteLine($"  SKIP: Sample patch not found at {patchPath}");
}

Console.WriteLine();

// Test 3: PatchExplainerService
Console.WriteLine("[3] Testing PatchExplainerService...");
var explainerLogger = NullLogger<PatchExplainerService>.Instance;
var explainer = new PatchExplainerService(explainerLogger, catalog);

if (File.Exists(patchPath))
{
    var patch = patchReader.ReadPatch(patchPath);
    var explanation = explainer.ExplainPatch(patch, patchPath);
    Console.WriteLine($"  OK Generated explanation ({explanation.Length} chars):");
    var lines = explanation.Split('\n');
    foreach (var line in lines.Take(25))
    {
        Console.WriteLine($"    {line}");
    }
    if (lines.Length > 25) Console.WriteLine($"    ... ({lines.Length - 25} more lines)");
}

Console.WriteLine();
Console.WriteLine("=== All tests passed ===");
return 0;
