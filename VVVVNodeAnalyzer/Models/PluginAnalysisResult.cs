using System;
using System.Collections.Generic;

namespace VvvvPluginAnalyzer.Models
{
    public class PluginAnalysisResult
    {
        public string PluginPath { get; set; } = "";
        public DateTime AnalysisDate { get; set; }
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public PackageInfo? PackageInfo { get; set; }
        public List<VLDocument> VLDocuments { get; set; } = new();
        public VLDocument VLLibraryDocument { get; set; } = new();
        public List<DotNetLibraryInfo> DotNetLibraries { get; set; } = new();
        public HelpSystemInfo HelpSystem { get; set; } = new();
        public DirectoryStructureInfo DirectoryStructure { get; set; } = new();
        public List<NodeSummary> AllNodes { get; set; } = new();
        public List<NugetDependency> Dependencies { get; set; } = new();
        public PluginType Type { get; set; }
    }

    public enum PluginType
    {
        VLDocument,      // Traditional VL-based plugin
        DotNetLibrary,   // Pure .NET library package
        Hybrid          // Contains both VL documents and .NET libraries
    }
}