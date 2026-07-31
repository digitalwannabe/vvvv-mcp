using System.Collections.Generic;

namespace VvvvPluginAnalyzer.Models
{
    public class PackageInfo
    {
        public string Id { get; set; } = "";
        public string Version { get; set; } = "";
        public string Title { get; set; } = "";
        public string Authors { get; set; } = "";
        public string Description { get; set; } = "";
        public string ProjectUrl { get; set; } = "";
        public string LicenseUrl { get; set; } = "";
        public string Tags { get; set; } = "";
    }
    
}