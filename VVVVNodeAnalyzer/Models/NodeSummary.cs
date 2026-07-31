using System.Collections.Generic;

namespace VvvvPluginAnalyzer.Models
{
    public class NodeSummary
    {
        public string Name { get; set; } = "";
        public string Id { get; set; } = "";
        public string Document { get; set; } = "";
        public string Patch { get; set; } = "";
        public string Category { get; set; } = "";
        public string Dependency { get; set; } = "";
        public string Operation { get; set; } = "";
        public string Source { get; set; } = ""; // "VL" or "DotNet"
        public List<PinInfo> InputPins { get; set; } = new();
        public List<PinInfo> OutputPins { get; set; } = new();
    }
}