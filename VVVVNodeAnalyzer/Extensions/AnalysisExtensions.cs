using System;
using System.Collections.Generic;
using System.Linq;
using VvvvPluginAnalyzer.Models;

namespace VvvvPluginAnalyzer.Extensions
{
    public static class AnalysisExtensions
    {
        public static Dictionary<string, int> GetNodeUsageStatistics(this PluginAnalysisResult result)
        {
            var usage = new Dictionary<string, int>();
            
            foreach (var doc in result.VLDocuments)
            {
                foreach (var patch in doc.Patches)
                {
                    foreach (var node in patch.Nodes)
                    {
                        var key = $"{node.NodeReference?.LastCategoryFullName}.{node.Name}";
                        usage[key] = usage.GetValueOrDefault(key, 0) + 1;
                    }
                }
            }
            
            return usage.OrderByDescending(kvp => kvp.Value).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public static List<string> GetAllCategories(this PluginAnalysisResult result)
        {
            return result.AllNodes
                .Select(n => n.Category)
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .OrderBy(c => c)
                .ToList();
        }

        public static Dictionary<string, List<NodeSummary>> GetNodesByCategory(this PluginAnalysisResult result)
        {
            return result.AllNodes
                .GroupBy(n => n.Category)
                .ToDictionary(g => g.Key, g => g.OrderBy(n => n.Name).ToList());
        }

        public static List<NodeSummary> FindNodesWithOperation(this PluginAnalysisResult result, string operation)
        {
            return result.AllNodes
                .Where(n => n.Operation.Equals(operation, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public static Dictionary<string, int> GetPinTypeStatistics(this PluginAnalysisResult result)
        {
            var typeStats = new Dictionary<string, int>();
            
            foreach (var node in result.AllNodes)
            {
                foreach (var pin in node.InputPins.Concat(node.OutputPins))
                {
                    var typeName = pin.TypeAnnotation?.Choices.FirstOrDefault()?.Name ?? "Unknown";
                    typeStats[typeName] = typeStats.GetValueOrDefault(typeName, 0) + 1;
                }
            }
            
            return typeStats.OrderByDescending(kvp => kvp.Value).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public static List<NodeSummary> FindNodesWithPinType(this PluginAnalysisResult result, string typeName)
        {
            return result.AllNodes
                .Where(n => n.InputPins.Concat(n.OutputPins)
                    .Any(p => p.TypeAnnotation?.Choices.Any(c => c.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase)) == true))
                .ToList();
        }

        public static PluginComplexityMetrics CalculateComplexityMetrics(this PluginAnalysisResult result)
        {
            var metrics = new PluginComplexityMetrics();
            
            foreach (var doc in result.VLDocuments)
            {
                foreach (var patch in doc.Patches)
                {
                    metrics.TotalNodes += patch.Nodes.Count;
                    metrics.TotalPads += patch.Pads.Count;
                    metrics.TotalLinks += patch.Links.Count;
                    
                    // Calculate patch complexity (nodes + links as a simple metric)
                    var patchComplexity = patch.Nodes.Count + patch.Links.Count;
                    if (patchComplexity > metrics.MaxPatchComplexity)
                    {
                        metrics.MaxPatchComplexity = patchComplexity;
                        metrics.MostComplexPatch = $"{doc.FileName}:{patch.Name}";
                    }
                }
            }
            
            metrics.AverageNodesPerPatch = result.VLDocuments.SelectMany(d => d.Patches).Any() 
                ? (double)metrics.TotalNodes / result.VLDocuments.SelectMany(d => d.Patches).Count()
                : 0;
                
            metrics.UniqueNodeTypes = result.AllNodes.Select(n => $"{n.Category}.{n.Name}").Distinct().Count();
            metrics.DotNetNodeCount = result.AllNodes.Count(n => n.Source == "DotNet");
            metrics.VLNodeCount = result.AllNodes.Count(n => n.Source == "VL");
            
            return metrics;
        }

        public static List<DotNetLibraryInfo> GetLibrariesWithDocumentation(this PluginAnalysisResult result)
        {
            return result.DotNetLibraries.Where(lib => lib.HasXmlDocumentation).ToList();
        }

        public static Dictionary<string, int> GetNamespaceStatistics(this PluginAnalysisResult result)
        {
            var namespaceStats = new Dictionary<string, int>();
            
            foreach (var library in result.DotNetLibraries)
            {
                foreach (var ns in library.Namespaces)
                {
                    namespaceStats[ns] = namespaceStats.GetValueOrDefault(ns, 0) + 1;
                }
            }
            
            return namespaceStats.OrderByDescending(kvp => kvp.Value).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
    }

    public class PluginComplexityMetrics
    {
        public int TotalNodes { get; set; }
        public int TotalPads { get; set; }
        public int TotalLinks { get; set; }
        public int UniqueNodeTypes { get; set; }
        public int VLNodeCount { get; set; }
        public int DotNetNodeCount { get; set; }
        public double AverageNodesPerPatch { get; set; }
        public int MaxPatchComplexity { get; set; }
        public string MostComplexPatch { get; set; } = "";
    }
}