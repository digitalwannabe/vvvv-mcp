using System;
using System.Collections.Generic;

namespace VvvvPluginAnalyzer.Models
{
    /// <summary>
    /// Simplified node information for MCP usage - contains only essential information
    /// </summary>
    public class UsableNode
    {
        public string Name { get; set; } = "";
        /// <summary>
        /// Version label from the node name, e.g. "Count" from "Split (Count)".
        /// Empty means the node has no version qualifier.
        /// </summary>
        public string Version { get; set; } = "";
        public string Category { get; set; } = "";
        /// <summary>Category.Name, or Category.Name (Version) when Version is set.</summary>
        public string FullName { get; set; } = "";
        public UsableNodeType Type { get; set; }
        public string Summary { get; set; } = "";
        public string Remarks { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public bool IsGeneric { get; set; }
        public bool HasState { get; set; }
        public List<UsablePin> Inputs { get; set; } = new();
        public List<UsablePin> Outputs { get; set; } = new();
        public string Source { get; set; } = ""; // Document name
    }

    public class UsablePin
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string Summary { get; set; } = "";
        public string DefaultValue { get; set; } = "";
        public bool IsOptional { get; set; }
        public bool IsGeneric { get; set; }
    }

    public class UsableNodesCollection
    {
        public string LibraryName { get; set; } = "";
        public string Version { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime ExtractionDate { get; set; }
        public List<UsableNode> Nodes { get; set; } = new();
        public List<string> Categories { get; set; } = new();
        public int TotalNodes { get; set; }
        public Dictionary<string, int> NodesByType { get; set; } = new();
    }

    public enum UsableNodeType
    {
        Unknown,
        Record,
        Class,
        Process,
        Operation,
        Method,
        Setter,
        Getter
    }
}