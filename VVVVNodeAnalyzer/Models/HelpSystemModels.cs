using System.Collections.Generic;

namespace VvvvPluginAnalyzer.Models
{
    public class HelpSystemInfo
    {
        public bool HasHelpXml { get; set; }
        public object? HelpXmlStructure { get; set; }
        public List<string> HelpXmlErrors { get; set; } = new();
        public List<HelpPatchInfo> HelpPatches { get; set; } = new();
    }

    public class HelpPatchInfo
    {
        public string FilePath { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public HelpPatchType Type { get; set; }
    }

    public enum HelpPatchType
    {
        Unknown,
        Explanation,
        HowTo,
        Reference,
        Tutorial,
        Example
    }

}