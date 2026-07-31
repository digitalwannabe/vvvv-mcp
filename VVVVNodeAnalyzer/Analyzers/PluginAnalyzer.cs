using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VvvvPluginAnalyzer.Analyzers;
using VvvvPluginAnalyzer.Models;

namespace VvvvPluginAnalyzer.Core
{
    public class PluginAnalyzer
    {
        private readonly VLLibraryAnalyzer _vlLibAnalyzer;
        private readonly UsableNodeExtractor _nodeExtractor;
        private readonly DotNetLibraryAnalyzer _dotNetAnalyzer;
        private readonly HelpSystemAnalyzer _helpAnalyzer;
        private readonly DirectoryAnalyzer _directoryAnalyzer;
        private readonly PackageAnalyzer _packageAnalyzer;

        public PluginAnalyzer(IEnumerable<string>? extraAssemblySearchDirs = null)
        {
            _vlLibAnalyzer = new VLLibraryAnalyzer();
            _nodeExtractor = new UsableNodeExtractor();
            _dotNetAnalyzer = new DotNetLibraryAnalyzer(extraAssemblySearchDirs);
            _helpAnalyzer = new HelpSystemAnalyzer();
            _directoryAnalyzer = new DirectoryAnalyzer();
            _packageAnalyzer = new PackageAnalyzer();
        }

        public PluginAnalysisResult AnalyzePlugin(string pluginDirectory)
        {
            var result = new PluginAnalysisResult
            {
                PluginPath = pluginDirectory,
                AnalysisDate = DateTime.Now,
                IsValid = true
            };

            try
            {
                // Analyze package info
                result.PackageInfo = _packageAnalyzer.AnalyzePackage(pluginDirectory);

                // Analyze directory structure
                result.DirectoryStructure = _packageAnalyzer.AnalyzeDirectoryStructure(pluginDirectory);

                // Analyze VL library
                result.VLLibraryDocument = _vlLibAnalyzer.AnalyzeVLLibrary(pluginDirectory);

                // Analyze .NET libraries
                result.DotNetLibraries = _dotNetAnalyzer.AnalyzeLibraries(pluginDirectory);

                // Analyze help system
                result.HelpSystem = _helpAnalyzer.AnalyzeHelpSystem(pluginDirectory);

                // Extract all dependencies
                result.Dependencies = ExtractAllDependencies(result);

                // Determine plugin type
                result.Type = DeterminePluginType(result);

                // Create unified node list
                result.AllNodes = CreateUnifiedNodeList(result);

                Console.WriteLine($" analysis completed. Found {result.AllNodes.Count} nodes from {result.VLLibraryDocument.NodeDefinitions.Count} VL definitions and {result.DotNetLibraries.Sum(l => l.AvailableNodes.Count)} .NET nodes.");
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Errors.Add($"Analysis failed: {ex.Message}");
            }

            return result;
        }

        public UsableNodesCollection ExtractUsableNodes(PluginAnalysisResult analysisResult)
        {
            var libraryName = analysisResult.PackageInfo?.Id ?? 
                             Path.GetFileName(analysisResult.PluginPath);

            var collection = _nodeExtractor.ExtractUsableNodes(analysisResult.VLLibraryDocument, libraryName);
            
            // Set additional metadata
            collection.Version = analysisResult.PackageInfo?.Version ?? "";
            collection.Description = analysisResult.PackageInfo?.Description ?? "";

            return collection;
        }

        private List<NugetDependency> ExtractAllDependencies(PluginAnalysisResult result)
        {
            var dependencies = new Dictionary<string, NugetDependency>();

            // From VL documents

                foreach (var dep in result.VLLibraryDocument.NugetDependencies)
                {
                    dependencies[dep.Location] = dep;
                }
            

            return dependencies.Values.ToList();
        }

        private PluginType DeterminePluginType(PluginAnalysisResult result)
        {
            var hasVLNodes = result.VLLibraryDocument.NodeDefinitions.Any();
            var hasDotNetNodes = result.DotNetLibraries.Any(l => l.AvailableNodes.Any());

            if (hasVLNodes && hasDotNetNodes)
                return PluginType.Hybrid;
            else if (hasDotNetNodes)
                return PluginType.DotNetLibrary;
            else
                return PluginType.VLDocument;
        }

        private List<NodeSummary> CreateUnifiedNodeList(PluginAnalysisResult result)
        {
            var allNodes = new List<NodeSummary>();

            // Add VL nodes

                foreach (var nodeDef in result.VLLibraryDocument.NodeDefinitions)
                {
                    // Skip Application nodes and other non-library nodes
                    if (nodeDef.Name == "Application" || string.IsNullOrEmpty(nodeDef.Category))
                        continue;

                    var nodeSummary = new NodeSummary
                    {
                        Name = nodeDef.Name,
                        Id = nodeDef.Name, // Use name as ID for VL nodes
                        Document = result.VLLibraryDocument.FileName,
                        Category = nodeDef.Category,
                        Operation = nodeDef.Type.ToString(),
                        Source = "VL",
                        InputPins = nodeDef.InputPins.Select(p => new PinInfo
                        {
                            Name = p.Name,
                            Kind = p.Kind,
                            DefaultValue = p.DefaultValue,
                            IsHidden = p.IsHidden
                        }).ToList(),
                        OutputPins = nodeDef.OutputPins.Select(p => new PinInfo
                        {
                            Name = p.Name,
                            Kind = p.Kind,
                            IsHidden = p.IsHidden
                        }).ToList()
                    };

                    allNodes.Add(nodeSummary);
                }
            

            // Add .NET nodes
            foreach (var lib in result.DotNetLibraries)
            {
                foreach (var node in lib.AvailableNodes)
                {
                    allNodes.Add(new NodeSummary
                    {
                        Name = node.Name,
                        Id = node.FullName,
                        Document = lib.AssemblyName,
                        Category = node.Category,
                        Operation = node.NodeType,
                        Source = "DotNet",
                        InputPins = node.InputPins.Select(p => new PinInfo
                        {
                            Name = p.Name,
                            Kind = "InputPin",
                            DefaultValue = p.DefaultValue ?? ""
                        }).ToList(),
                                                OutputPins = node.OutputPins.Select(p => new PinInfo
                        {
                            Name = p.Name,
                            Kind = "OutputPin",
                            DefaultValue = ""
                        }).ToList()
                    });
                }
            }

            return allNodes;
        }
    }
}

