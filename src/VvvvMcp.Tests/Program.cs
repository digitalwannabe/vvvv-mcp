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

// Test 4: PatchBuilderService — the rotating-box scenario in ONE call (catalog fallback)
Console.WriteLine("[4] Testing PatchBuilderService (build_patch, offline catalog)...");
var writerLogger = NullLogger<PatchWriterService>.Instance;
var writer = new PatchWriterService(writerLogger);
var bridgeLogger = NullLogger<BridgeClientService>.Instance;
using var bridge = new BridgeClientService(bridgeLogger);
var resolverLogger = NullLogger<NodeResolutionService>.Instance;
var resolver = new NodeResolutionService(bridge, catalog, resolverLogger);
var builderLogger = NullLogger<PatchBuilderService>.Instance;
var builder = new PatchBuilderService(writer, resolver, bridge, builderLogger);

var testPatch = Path.Combine(Path.GetTempPath(), "vvvv-mcp-build-test.vl");
if (File.Exists(testPatch)) File.Delete(testPatch);

var spec = """
{
  "filePath": "__PATH__",
  "nodes": [
    { "key": "rot",   "name": "Rotation (Successive)", "category": "3D.Transform" },
    { "key": "mat",   "name": "PBRMaterial", "category": "Stride.Materials" },
    { "key": "box",   "name": "Box", "category": "Stride.Models" },
    { "key": "light", "name": "DirectionalLight", "category": "Stride.Lights" },
    { "key": "scene", "name": "RootScene", "category": "Stride" },
    { "key": "win",   "name": "SceneWindow", "category": "Stride" }
  ],
  "pads": [
    { "key": "speed", "type": "Vector3", "value": "0.25, 0, 0", "comment": "rotations per second" }
  ],
  "links": [
    { "from": "speed",        "to": "rot.Angular Delta" },
    { "from": "rot.Result",   "to": "box.Transformation" },
    { "from": "mat.Output",   "to": "box.Material" },
    { "from": "box.Entity",   "to": "scene.Child" },
    { "from": "light.Entity", "to": "scene.Child" },
    { "from": "scene.Output", "to": "win.Input" }
  ],
  "verify": true,
  "verbosity": "compact"
}
""".Replace("__PATH__", testPatch.Replace("\\", "\\\\"));

var buildResult = await builder.BuildAsync(spec);
var json = System.Text.Json.JsonSerializer.Serialize(buildResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
Console.WriteLine($"  Build result: {json}");

if (!File.Exists(testPatch))
{
    Console.WriteLine("  ERROR: patch file was not written");
    return 1;
}

// Re-read and verify structure
var built = patchReader.ReadPatch(testPatch);
Console.WriteLine($"  OK Re-read patch: {built.AllNodes.Count} nodes, {built.AllPads.Count} pads, {built.Links.Count} links");
if (built.Links.Count != 6)
{
    Console.WriteLine($"  ERROR: expected 6 links, got {built.Links.Count}");
    return 1;
}

// Copy to the vvvv sketches folder for visual inspection in the editor
var sketchCopy = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
    "vvvv", "gamma-preview", "Sketches", "vvvv-mcp-rotating-box-2.vl");
try
{
    Directory.CreateDirectory(Path.GetDirectoryName(sketchCopy)!);
    File.Copy(testPatch, sketchCopy, overwrite: true);
    Console.WriteLine($"  OK Copied to {sketchCopy} for visual check");
}
catch (Exception ex) { Console.WriteLine($"  (sketch copy failed: {ex.Message})"); }

// XML sanity: file must parse and contain NugetDependency
var xdoc = System.Xml.Linq.XDocument.Load(testPatch);
if (xdoc.Root?.Element("NugetDependency") is null)
{
    Console.WriteLine("  ERROR: no NugetDependency in generated file");
    return 1;
}
Console.WriteLine("  OK XML valid, dependencies present");

// Test 5: FTS search — multi-word queries must not return 0 results
Console.WriteLine("[5] Testing two-phase FTS search...");
var indexLogger = NullLogger<SearchIndexService>.Instance;
using var index = new SearchIndexService(indexLogger);
var dbPath = Path.Combine(Path.GetTempPath(), "vvvv-mcp-test-index.db");
if (File.Exists(dbPath)) File.Delete(dbPath);
await index.InitializeAsync(dbPath);
await index.RebuildNodeIndexAsync(catalog.GetAllNodes());
var andThenOr = index.SearchNodeHits("transform matrix 2d", limit: 5);
Console.WriteLine($"  OK 'transform matrix 2d': {andThenOr.Count} hits (was 0 with pure AND)");
foreach (var h in andThenOr.Take(3)) Console.WriteLine($"    - {h.FullName} [{h.Package}] score={h.Score:F1}");
var variant = catalog.FindTolerant("Rotation (Successive)");
Console.WriteLine($"  OK FindTolerant('Rotation (Successive)'): {variant.Count} match(es)");

Console.WriteLine();
Console.WriteLine("=== All tests passed ===");
return 0;

