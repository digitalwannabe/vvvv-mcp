using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VvvvPluginAnalyzer.Core;
using VvvvPluginAnalyzer.Exporters;
using VvvvPluginAnalyzer.Extensions;
using VvvvPluginAnalyzer.Models;

namespace VvvvPluginAnalyzer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return;
            }

            // Batch mode: analyze all subfolders in a packs directory (excluding 'dependencies')
            if (args[0].Equals("batch", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Error: batch mode requires a packs folder path.");
                    Console.WriteLine("Usage: analyzer.exe batch <packs-folder> [output-dir]");
                    return;
                }

                var packsFolder = args[1];
                var outputDir = args.Length > 2 ? args[2] : packsFolder;
                RunBatchAnalysis(packsFolder, outputDir);
                return;
            }

            // Single-package mode (original behaviour)
            var pluginDirectory = args[0];
            var outputFormat = args.Length > 1 ? args[1].ToLower() : "json";
            var outputPath = args.Length > 2 ? args[2] : null;

            if (!Directory.Exists(pluginDirectory))
            {
                Console.WriteLine($"Error: Directory '{pluginDirectory}' does not exist.");
                return;
            }

            var analyzer = new PluginAnalyzer();
            var jsonExporter = new JsonExporter();
            var markdownExporter = new MarkdownExporter();
            var nodesExporter = new UsableNodesExporter();

            Console.WriteLine($"Analyzing plugin in: {pluginDirectory}");
            var result = analyzer.AnalyzePlugin(pluginDirectory);

            if (!result.IsValid)
            {
                Console.WriteLine("Analysis completed with errors:");
                foreach (var error in result.Errors)
                {
                    Console.WriteLine($"  - {error}");
                }
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("Analysis completed successfully!");
            }

            // Extract usable nodes
            var usableNodes = analyzer.ExtractUsableNodes(result);

            Console.WriteLine($"Plugin Type: {result.Type}");
            Console.WriteLine($"Found {result.AllNodes.Count} total nodes");
            Console.WriteLine($"  - VL Nodes: {result.AllNodes.Count(n => n.Source == "VL")}");
            Console.WriteLine($"  - .NET Nodes: {result.AllNodes.Count(n => n.Source == "DotNet")}");
            Console.WriteLine($"Usable Library Nodes: {usableNodes.TotalNodes}");
            Console.WriteLine($"Categories: {usableNodes.Categories.Count}");
            Console.WriteLine($"Dependencies: {result.Dependencies.Count}");
            Console.WriteLine($".NET Libraries: {result.DotNetLibraries.Count}");
            Console.WriteLine($"Help patches: {result.HelpSystem.HelpPatches.Count}");

            // Output results
            switch (outputFormat)
            {
                case "json":
                    var jsonOutput = outputPath ?? Path.Combine(pluginDirectory, "analysis.json");
                    File.WriteAllText(jsonOutput, jsonExporter.ExportToJson(result));
                    Console.WriteLine($"JSON analysis saved to: {jsonOutput}");
                    break;

                case "markdown":
                case "md":
                    var mdOutput = outputPath ?? Path.Combine(pluginDirectory, "analysis.md");
                    markdownExporter.ExportToMarkdown(result, mdOutput);
                    Console.WriteLine($"Markdown report saved to: {mdOutput}");
                    break;

                case "nodes-only":
                    var nodesJsonOutput = outputPath ?? Path.Combine(pluginDirectory, "usable-nodes.json");
                    var nodesMdOutput = Path.Combine(pluginDirectory, "usable-nodes.md");
                    nodesExporter.ExportToFile(usableNodes, nodesJsonOutput);
                    nodesExporter.ExportToMarkdown(usableNodes, nodesMdOutput);
                    Console.WriteLine($"Usable nodes saved to: {nodesJsonOutput} and {nodesMdOutput}");
                    break;

                case "both":
                    var jsonOut = Path.Combine(pluginDirectory, "analysis.json");
                    var mdOut = Path.Combine(pluginDirectory, "analysis.md");
                    var nodesJsonOut = Path.Combine(pluginDirectory, "usable-nodes.json");
                    var nodesMdOut = Path.Combine(pluginDirectory, "usable-nodes.md");

                    File.WriteAllText(jsonOut, jsonExporter.ExportToJson(result));
                    markdownExporter.ExportToMarkdown(result, mdOut);
                    nodesExporter.ExportToFile(usableNodes, nodesJsonOut);
                    nodesExporter.ExportToMarkdown(usableNodes, nodesMdOut);

                    Console.WriteLine($"Full analysis saved to: {jsonOut} and {mdOut}");
                    Console.WriteLine($"Usable nodes saved to: {nodesJsonOut} and {nodesMdOut}");
                    break;

                default:
                    Console.WriteLine("Unknown output format. Using JSON.");
                    var defaultOutput = outputPath ?? Path.Combine(pluginDirectory, "analysis.json");
                    File.WriteAllText(defaultOutput, jsonExporter.ExportToJson(result));
                    Console.WriteLine($"JSON analysis saved to: {defaultOutput}");
                    break;
            }

            // Print summary to console
            PrintSummary(result, usableNodes);
        }

        // -----------------------------------------------------------------------
        // Batch mode
        // -----------------------------------------------------------------------

        private static void RunBatchAnalysis(string packsFolder, string outputDir)
        {
            if (!Directory.Exists(packsFolder))
            {
                Console.WriteLine($"Error: Packs folder '{packsFolder}' does not exist.");
                return;
            }

            Directory.CreateDirectory(outputDir);

            var subDirs = Directory.GetDirectories(packsFolder)
                .Where(d => {
                    var name = Path.GetFileName(d);
                    // Skip the dependency cache
                    if (name.Equals("dependencies", StringComparison.OrdinalIgnoreCase)) return false;
                    // Skip vvvv editor-internal packages — they expose HDE/editor nodes
                    // that are not relevant to user-facing patching or MCP catalog queries.
                    // VL.HDE is the main editor package; *_HDE_* are editor-only plugins.
                    if (name.Equals("VL.HDE", StringComparison.OrdinalIgnoreCase)) return false;
                    if (name.StartsWith("VL.HDE.", StringComparison.OrdinalIgnoreCase)) return false;
                    if (name.Contains("_HDE_", StringComparison.OrdinalIgnoreCase)) return false;
                    if (name.EndsWith(".HDE", StringComparison.OrdinalIgnoreCase)) return false;
                    return true;
                })
                .OrderBy(d => d)
                .ToList();

            Console.WriteLine($"Batch analysis of {subDirs.Count} packages in: {packsFolder}");
            Console.WriteLine($"Output directory: {outputDir}");
            Console.WriteLine(new string('-', 60));

            // Collect extra directories so the DotNet analyzer can resolve dependencies
            // that live in the vvvv install root (VL.Core.dll, Stride.Core.dll, …)
            var extraSearchDirs = new List<string>();
            var vvvvRoot = Path.GetDirectoryName(packsFolder);
            if (!string.IsNullOrEmpty(vvvvRoot) && Directory.Exists(vvvvRoot))
                extraSearchDirs.Add(vvvvRoot);
            var depsDir = Path.Combine(packsFolder, "dependencies");
            if (Directory.Exists(depsDir))
                extraSearchDirs.Add(depsDir);

            var analyzer = new PluginAnalyzer(extraSearchDirs);
            var allCollections = new List<UsableNodesCollection>();
            int succeeded = 0;
            int failed = 0;

            foreach (var subDir in subDirs)
            {
                var packageName = Path.GetFileName(subDir);
                Console.Write($"  [{succeeded + failed + 1}/{subDirs.Count}] {packageName} ... ");

                try
                {
                    var result = analyzer.AnalyzePlugin(subDir);

                    if (!result.IsValid)
                    {
                        Console.WriteLine($"WARN ({string.Join("; ", result.Errors)})");
                        // Still try to extract whatever nodes were found
                    }

                    var collection = analyzer.ExtractUsableNodes(result);
                    allCollections.Add(collection);

                    Console.WriteLine($"OK ({collection.TotalNodes} nodes)");
                    succeeded++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FAILED: {ex.Message}");
                    failed++;
                }
            }

            Console.WriteLine(new string('-', 60));
            Console.WriteLine($"Done: {succeeded} succeeded, {failed} failed.");

            if (allCollections.Count == 0)
            {
                Console.WriteLine("No nodes extracted — nothing to write.");
                return;
            }

            var merged = MergeCollections(allCollections);

            var nodesExporter = new UsableNodesExporter();
            var jsonPath = Path.Combine(outputDir, "vvvv_nodes_mcp.json");
            var mdPath = Path.Combine(outputDir, "vvvv_nodes_mcp.md");

            nodesExporter.ExportToFile(merged, jsonPath);
            nodesExporter.ExportToMarkdown(merged, mdPath);

            Console.WriteLine();
            Console.WriteLine($"Merged {merged.TotalNodes} nodes across {merged.Categories.Count} categories.");
            Console.WriteLine($"JSON saved to : {jsonPath}");
            Console.WriteLine($"Markdown saved to: {mdPath}");
        }

        /// <summary>
        /// Merges multiple <see cref="UsableNodesCollection"/> instances into a single collection,
        /// combining all nodes, de-duplicating categories, and summing per-type counts.
        /// </summary>
        private static UsableNodesCollection MergeCollections(IEnumerable<UsableNodesCollection> collections)
        {
            var merged = new UsableNodesCollection
            {
                LibraryName = "vvvv Gamma Core",
                Version = "",
                Description = "Merged node dictionary from all vvvv Gamma core packages.",
                ExtractionDate = DateTime.Now,
                Nodes = new List<UsableNode>(),
                Categories = new List<string>(),
                NodesByType = new Dictionary<string, int>()
            };

            foreach (var col in collections)
            {
                merged.Nodes.AddRange(col.Nodes);

                foreach (var kvp in col.NodesByType)
                {
                    if (merged.NodesByType.TryGetValue(kvp.Key, out var existing))
                        merged.NodesByType[kvp.Key] = existing + kvp.Value;
                    else
                        merged.NodesByType[kvp.Key] = kvp.Value;
                }
            }

            // Rebuild categories from actual nodes (sorted, unique)
            merged.Categories = merged.Nodes
                .Select(n => n.Category)
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            merged.TotalNodes = merged.Nodes.Count;

            return merged;
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static void PrintUsage()
        {
            Console.WriteLine("vvvv Gamma Plugin Analyzer v2.2");
            Console.WriteLine(" VL library analysis with usable nodes extraction");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  Single package:");
            Console.WriteLine("    analyzer.exe <plugin-directory> [output-format] [output-path]");
            Console.WriteLine("    Output formats: json, markdown, both, nodes-only");
            Console.WriteLine("    Example: analyzer.exe \"C:\\vvvv\\packages\\VL.Devices.Leap\" json");
            Console.WriteLine();
            Console.WriteLine("  Batch (all subfolders except 'dependencies'):");
            Console.WriteLine("    analyzer.exe batch <packs-folder> [output-dir]");
            Console.WriteLine("    Example: analyzer.exe batch \"C:\\Program Files\\vvvv\\...\\packs\" \"C:\\output\"");
        }

        private static void PrintSummary(PluginAnalysisResult result, UsableNodesCollection usableNodes)
        {
            Console.WriteLine("\n=== ANALYSIS SUMMARY ===");

            if (result.PackageInfo != null)
            {
                Console.WriteLine($"Package: {result.PackageInfo.Id} v{result.PackageInfo.Version}");
                Console.WriteLine($"Title: {result.PackageInfo.Title}");
                Console.WriteLine($"Authors: {result.PackageInfo.Authors}");
            }

            Console.WriteLine($"\nPlugin Type: {result.Type}");
            Console.WriteLine($"VL Documents: {result.VLDocuments.Count}");
            Console.WriteLine($".NET Libraries: {result.DotNetLibraries.Count}");
            Console.WriteLine($"Dependencies: {result.Dependencies.Count}");

            if (result.Dependencies.Any())
            {
                Console.WriteLine("  Key Dependencies:");
                foreach (var dep in result.Dependencies.Take(5))
                {
                    Console.WriteLine($"    - {dep.Location} v{dep.Version}");
                }
                if (result.Dependencies.Count > 5)
                    Console.WriteLine($"    ... and {result.Dependencies.Count - 5} more");
            }

            // Show .NET library summary
            if (result.DotNetLibraries.Any())
            {
                Console.WriteLine($"\n.NET Libraries:");
                foreach (var lib in result.DotNetLibraries.Take(5))
                {
                    Console.WriteLine($"  {lib.AssemblyName} v{lib.Version} ({lib.AvailableNodes.Count} nodes)");
                }
                if (result.DotNetLibraries.Count > 5)
                    Console.WriteLine($"  ... and {result.DotNetLibraries.Count - 5} more");
            }

            // Show usable nodes summary
            Console.WriteLine($"\n=== USABLE NODES SUMMARY ===");
            Console.WriteLine($"Total Usable Nodes: {usableNodes.TotalNodes}");
            Console.WriteLine($"Categories: {usableNodes.Categories.Count}");

            if (usableNodes.NodesByType.Any())
            {
                Console.WriteLine("\nNodes by Type:");
                foreach (var kvp in usableNodes.NodesByType.OrderByDescending(x => x.Value))
                {
                    Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
                }
            }

            Console.WriteLine($"\nNodes by Category:");
            var nodesByCategory = usableNodes.Nodes.GroupBy(n => n.Category).OrderByDescending(g => g.Count());
            foreach (var category in nodesByCategory.Take(10))
            {
                Console.WriteLine($"  {category.Key}: {category.Count()} nodes");
            }
            if (nodesByCategory.Count() > 10)
                Console.WriteLine($"  ... and {nodesByCategory.Count() - 10} more categories");

            // Show some example nodes
            if (usableNodes.Nodes.Any())
            {
                Console.WriteLine($"\nExample Nodes:");
                foreach (var node in usableNodes.Nodes.Take(5))
                {
                    var inputCount = node.Inputs.Count;
                    var outputCount = node.Outputs.Count;
                    var genericMarker = node.IsGeneric ? " (Generic)" : "";
                    var stateMarker = node.HasState ? " (Stateful)" : "";

                    Console.WriteLine($"  {node.FullName}{genericMarker}{stateMarker}");
                    Console.WriteLine($"    Inputs: {inputCount}, Outputs: {outputCount}");
                    if (!string.IsNullOrEmpty(node.Summary))
                    {
                        Console.WriteLine($"    Summary: {node.Summary}");
                    }
                }
                if (usableNodes.Nodes.Count > 5)
                    Console.WriteLine($"  ... and {usableNodes.Nodes.Count - 5} more nodes");
            }

            if (result.HelpSystem.HelpPatches.Any())
            {
                Console.WriteLine($"\nHelp System:");
                Console.WriteLine($"  Total patches: {result.HelpSystem.HelpPatches.Count}");
                var helpTypes = result.HelpSystem.HelpPatches.GroupBy(h => h.Type);
                foreach (var type in helpTypes)
                {
                    Console.WriteLine($"    {type.Key}: {type.Count()}");
                }
            }

            Console.WriteLine($"\nDirectory Structure:");
            Console.WriteLine($"  /lib: {(result.DirectoryStructure.HasLibDir ? "✓" : "✗")} ({result.DirectoryStructure.LibFiles.Count} files, {result.DirectoryStructure.DotNetLibraries.Count} .NET libraries)");
            Console.WriteLine($"  /runtimes: {(result.DirectoryStructure.HasRuntimesDir ? "✓" : "✗")} ({result.DirectoryStructure.RuntimeFiles.Count} files)");
            Console.WriteLine($"  /src: {(result.DirectoryStructure.HasSrcDir ? "✓" : "✗")} ({result.DirectoryStructure.SourceFiles.Count} files)");
            Console.WriteLine($"  /help: {(result.DirectoryStructure.HasHelpDir ? "✓" : "✗")}");

            // Show complexity metrics
            var metrics = result.CalculateComplexityMetrics();
            Console.WriteLine($"\nComplexity Metrics:");
            Console.WriteLine($"  Total Nodes: {metrics.TotalNodes} (VL: {metrics.VLNodeCount}, .NET: {metrics.DotNetNodeCount})");
            Console.WriteLine($"  Unique Node Types: {metrics.UniqueNodeTypes}");
            if (metrics.AverageNodesPerPatch > 0)
                Console.WriteLine($"  Average Nodes per Patch: {metrics.AverageNodesPerPatch:F1}");
            if (!string.IsNullOrEmpty(metrics.MostComplexPatch))
                Console.WriteLine($"  Most Complex Patch: {metrics.MostComplexPatch} ({metrics.MaxPatchComplexity} elements)");
        }
    }
}
