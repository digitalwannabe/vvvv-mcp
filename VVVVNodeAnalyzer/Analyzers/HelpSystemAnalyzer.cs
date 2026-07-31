using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using VvvvPluginAnalyzer.Models;

namespace VvvvPluginAnalyzer.Analyzers
{
    public class HelpSystemAnalyzer
    {
        public HelpSystemInfo AnalyzeHelpSystem(string pluginDirectory)
        {
            var helpInfo = new HelpSystemInfo();
            var helpDir = Path.Combine(pluginDirectory, "help");

            if (!Directory.Exists(helpDir))
                return helpInfo;

            // Look for Help.xml
            var helpXmlPath = Path.Combine(helpDir, "Help.xml");
            if (File.Exists(helpXmlPath))
            {
                helpInfo.HasHelpXml = true;
                try
                {
                    var doc = XDocument.Load(helpXmlPath);
                    helpInfo.HelpXmlStructure = AnalyzeHelpXml(doc);
                }
                catch (Exception ex)
                {
                    helpInfo.HelpXmlErrors.Add($"Error parsing Help.xml: {ex.Message}");
                }
            }

            // Scan for help patches
            var helpFiles = Directory.GetFiles(helpDir, "*.vl", SearchOption.AllDirectories);
            foreach (var helpFile in helpFiles)
            {
                var relativePath = Path.GetRelativePath(helpDir, helpFile);
                var fileName = Path.GetFileName(helpFile);
                
                var helpPatch = new HelpPatchInfo
                {
                    FilePath = helpFile,
                    RelativePath = relativePath,
                    FileName = fileName,
                    Type = DetermineHelpPatchType(fileName)
                };

                helpInfo.HelpPatches.Add(helpPatch);
            }

            return helpInfo;
        }

        private HelpPatchType DetermineHelpPatchType(string fileName)
        {
            if (fileName.StartsWith("Explanation", StringComparison.OrdinalIgnoreCase))
                return HelpPatchType.Explanation;
            if (fileName.StartsWith("HowTo", StringComparison.OrdinalIgnoreCase))
                return HelpPatchType.HowTo;
            if (fileName.StartsWith("Reference", StringComparison.OrdinalIgnoreCase))
                return HelpPatchType.Reference;
            if (fileName.StartsWith("Tutorial", StringComparison.OrdinalIgnoreCase))
                return HelpPatchType.Tutorial;
            if (fileName.StartsWith("Example", StringComparison.OrdinalIgnoreCase))
                return HelpPatchType.Example;
            
            return HelpPatchType.Unknown;
        }

        private object AnalyzeHelpXml(XDocument doc)
        {
            // Simplified help XML structure analysis
            return new
            {
                HasTopics = doc.Root?.Elements("Topic").Any() ?? false,
                TopicCount = doc.Root?.Elements("Topic").Count() ?? 0,
                HasUriItems = doc.Root?.Descendants("UriItem").Any() ?? false,
                HasVLDocuments = doc.Root?.Descendants("VLDocument").Any() ?? false
            };
        }
    }
}