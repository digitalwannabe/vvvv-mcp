using System.IO;
using System.Linq;
using System.Text;
using VvvvPluginAnalyzer.Models;

namespace VvvvPluginAnalyzer.Exporters
{
    public class MarkdownExporter
    {
        public void ExportToMarkdown(PluginAnalysisResult result, string outputPath)
        {
            var md = new StringBuilder();
            
            md.AppendLine($"# Plugin Analysis Report: {result.PackageInfo?.Id ?? "Unknown"}");
            md.AppendLine($"*Generated on {result.AnalysisDate:yyyy-MM-dd HH:mm:ss}*");
            md.AppendLine();

            // Plugin Type
            md.AppendLine($"**Plugin Type:** {result.Type}");
            md.AppendLine();

            // Package Info
            if (result.PackageInfo != null)
            {
                md.AppendLine("## Package Information");
                md.AppendLine($"**ID:** {result.PackageInfo.Id}");
                md.AppendLine($"**Version:** {result.PackageInfo.Version}");
                md.AppendLine($"**Title:** {result.PackageInfo.Title}");
                md.AppendLine($"**Authors:** {result.PackageInfo.Authors}");
                md.AppendLine($"**Description:** {result.PackageInfo.Description}");
                if (!string.IsNullOrEmpty(result.PackageInfo.Tags))
                    md.AppendLine($"**Tags:** {result.PackageInfo.Tags}");
                md.AppendLine();
            }

            // Dependencies
            if (result.Dependencies.Any())
            {
                md.AppendLine("## Dependencies");
                foreach (var dep in result.Dependencies)
                {
                    md.AppendLine($"- **{dep.Location}** (v{dep.Version})");
                }
                md.AppendLine();
            }

            // .NET Libraries Section
            if (result.DotNetLibraries.Any())
            {
                md.AppendLine("## .NET Libraries");
                md.AppendLine($"Found **{result.DotNetLibraries.Count}** .NET libraries providing **{result.DotNetLibraries.Sum(l => l.AvailableNodes.Count)}** nodes");
                md.AppendLine();

                foreach (var library in result.DotNetLibraries)
                {
                    md.AppendLine($"### {library.FileName}");
                    md.AppendLine($"- **Assembly:** {library.AssemblyName} v{library.Version}");
                    md.AppendLine($"- **Target Framework:** {library.TargetFramework}");
                    md.AppendLine($"- **Public Types:** {library.PublicTypes.Count}");
                    md.AppendLine($"- **Available Nodes:** {library.AvailableNodes.Count}");
                    md.AppendLine($"- **Namespaces:** {string.Join(", ", library.Namespaces.Take(5))}{(library.Namespaces.Count > 5 ? "..." : "")}");
                    if (library.HasXmlDocumentation)
                        md.AppendLine("- ✅ Has XML Documentation");
                    md.AppendLine();
                }
            }

            // VL Documents Section
            if (result.VLDocuments.Any())
            {
                md.AppendLine("## VL Documents");
                md.AppendLine($"Found **{result.VLDocuments.Count}** VL documents");
                md.AppendLine();

                                foreach (var doc in result.VLDocuments)
                {
                    md.AppendLine($"### {doc.FileName}");
                    md.AppendLine($"- **Document ID:** {doc.DocumentId}");
                    md.AppendLine($"- **Language Version:** {doc.LanguageVersion}");
                    md.AppendLine($"- **Patches:** {doc.Patches.Count}");
                    md.AppendLine($"- **Dependencies:** {doc.NugetDependencies.Count}");
                    md.AppendLine();
                }
            }

            // Nodes Overview
            if (result.AllNodes.Any())
            {
                md.AppendLine("## Nodes Overview");
                md.AppendLine($"Total unique nodes: **{result.AllNodes.Count}**");
                
                var vlNodes = result.AllNodes.Where(n => n.Source == "VL").Count();
                var dotNetNodes = result.AllNodes.Where(n => n.Source == "DotNet").Count();
                
                if (vlNodes > 0)
                    md.AppendLine($"- VL Nodes: **{vlNodes}**");
                if (dotNetNodes > 0)
                    md.AppendLine($"- .NET Nodes: **{dotNetNodes}**");
                md.AppendLine();

                var categories = result.AllNodes.GroupBy(n => new { n.Category, n.Source }).OrderBy(g => g.Key.Source).ThenBy(g => g.Key.Category);
                foreach (var categoryGroup in categories)
                {
                    md.AppendLine($"### {categoryGroup.Key.Category} ({categoryGroup.Key.Source})");
                    foreach (var node in categoryGroup.OrderBy(n => n.Name))
                    {
                        md.AppendLine($"#### {node.Name}");
                        if (!string.IsNullOrEmpty(node.Operation))
                            md.AppendLine($"*Operation: {node.Operation}*");
                        
                        if (node.InputPins.Any())
                        {
                            md.AppendLine("**Inputs:**");
                            foreach (var pin in node.InputPins)
                            {
                                var typeInfo = pin.TypeAnnotation?.Choices.FirstOrDefault()?.Name ?? "Unknown";
                                var optional = pin.Kind.Contains("Optional") ? " (Optional)" : "";
                                md.AppendLine($"- {pin.Name} ({typeInfo}){optional}");
                            }
                        }
                        
                        if (node.OutputPins.Any())
                        {
                            md.AppendLine("**Outputs:**");
                            foreach (var pin in node.OutputPins)
                            {
                                var typeInfo = pin.TypeAnnotation?.Choices.FirstOrDefault()?.Name ?? "Unknown";
                                md.AppendLine($"- {pin.Name} ({typeInfo})");
                            }
                        }
                        md.AppendLine();
                    }
                }
            }

            // Help System
            if (result.HelpSystem.HelpPatches.Any())
            {
                md.AppendLine("## Help System");
                if (result.HelpSystem.HasHelpXml)
                    md.AppendLine("✅ Has Help.xml structure file");
                
                md.AppendLine($"**Help patches found:** {result.HelpSystem.HelpPatches.Count}");
                
                var helpByType = result.HelpSystem.HelpPatches.GroupBy(h => h.Type);
                foreach (var group in helpByType)
                {
                    md.AppendLine($"- {group.Key}: {group.Count()}");
                }
                md.AppendLine();
            }

            // Directory Structure
            md.AppendLine("## Directory Structure");
            if (result.DirectoryStructure.HasLibDir)
                md.AppendLine($"✅ /lib directory ({result.DirectoryStructure.LibFiles.Count} files, {result.DirectoryStructure.DotNetLibraries.Count} .NET libraries)");
            if (result.DirectoryStructure.HasRuntimesDir)
                md.AppendLine($"✅ /runtimes directory ({result.DirectoryStructure.RuntimeFiles.Count} files)");
            if (result.DirectoryStructure.HasSrcDir)
                md.AppendLine($"✅ /src directory ({result.DirectoryStructure.SourceFiles.Count} files)");
            if (result.DirectoryStructure.HasHelpDir)
                md.AppendLine("✅ /help directory");
            md.AppendLine();

            // .NET Library Details
            if (result.DotNetLibraries.Any())
            {
                md.AppendLine("## .NET Library Details");
                foreach (var library in result.DotNetLibraries)
                {
                    md.AppendLine($"### {library.AssemblyName}");
                    md.AppendLine($"**File:** {library.FileName}");
                    md.AppendLine($"**Version:** {library.Version}");
                    md.AppendLine($"**Target Framework:** {library.TargetFramework}");
                    
                    if (library.ReferencedAssemblies.Any())
                    {
                        md.AppendLine("**Referenced Assemblies:**");
                        foreach (var refAssembly in library.ReferencedAssemblies.Take(10))
                        {
                            md.AppendLine($"- {refAssembly}");
                        }
                        if (library.ReferencedAssemblies.Count > 10)
                            md.AppendLine($"- ... and {library.ReferencedAssemblies.Count - 10} more");
                    }

                    if (library.Namespaces.Any())
                    {
                        md.AppendLine("**Namespaces:**");
                        foreach (var ns in library.Namespaces)
                        {
                            md.AppendLine($"- {ns}");
                        }
                    }
                    md.AppendLine();
                }
            }

            File.WriteAllText(outputPath, md.ToString());
        }
    }
}
