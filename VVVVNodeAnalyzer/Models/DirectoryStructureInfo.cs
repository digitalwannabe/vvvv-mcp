using System.Collections.Generic;

namespace VvvvPluginAnalyzer.Models
{
    public class DirectoryStructureInfo
    {
        public string RootPath { get; set; } = "";
        public bool HasLibDir { get; set; }
        public bool HasRuntimesDir { get; set; }
        public bool HasSrcDir { get; set; }
        public bool HasHelpDir { get; set; }
        public List<string> LibFiles { get; set; } = new();
        public List<string> DotNetLibraries { get; set; } = new();
        public List<string> RuntimeFiles { get; set; } = new();
        public List<string> SourceFiles { get; set; } = new();
    }
}