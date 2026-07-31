using System;
using System.IO;
using System.Xml.Linq;
using VvvvPluginAnalyzer.Models;

namespace VvvvPluginAnalyzer.Analyzers
{
    public class PackageAnalyzer
    {
        public PackageInfo? AnalyzePackage(string pluginDirectory)
        {
            // Look for .nuspec file
            var nuspecFiles = Directory.GetFiles(pluginDirectory, "*.nuspec", SearchOption.TopDirectoryOnly);
            if (nuspecFiles.Length > 0)
            {
                return AnalyzeNuspecFile(nuspecFiles[0]);
            }

            // Look for .csproj or .vbproj files
            var projectFiles = Directory.GetFiles(pluginDirectory, "*.csproj", SearchOption.AllDirectories);
            if (projectFiles.Length == 0)
            {
                projectFiles = Directory.GetFiles(pluginDirectory, "*.vbproj", SearchOption.AllDirectories);
            }

            if (projectFiles.Length > 0)
            {
                return AnalyzeProjectFile(projectFiles[0]);
            }

            // Fallback: create basic info from directory name
            return new PackageInfo
            {
                Id = Path.GetFileName(pluginDirectory),
                Title = Path.GetFileName(pluginDirectory),
                Version = "Unknown",
                Authors = "Unknown",
                Description = "No package information found"
            };
        }

        public DirectoryStructureInfo AnalyzeDirectoryStructure(string pluginDirectory)
        {
            var structure = new DirectoryStructureInfo
            {
                RootPath = pluginDirectory
            };

            // Check for standard directories
            var libDir = Path.Combine(pluginDirectory, "lib");
            var runtimesDir = Path.Combine(pluginDirectory, "runtimes");
            var srcDir = Path.Combine(pluginDirectory, "src");
            var helpDir = Path.Combine(pluginDirectory, "help");

            structure.HasLibDir = Directory.Exists(libDir);
            structure.HasRuntimesDir = Directory.Exists(runtimesDir);
            structure.HasSrcDir = Directory.Exists(srcDir);
            structure.HasHelpDir = Directory.Exists(helpDir);

            // Analyze lib directory
            if (structure.HasLibDir)
            {
                structure.LibFiles.AddRange(Directory.GetFiles(libDir, "*", SearchOption.AllDirectories));
                structure.DotNetLibraries.AddRange(Directory.GetFiles(libDir, "*.dll", SearchOption.AllDirectories));
            }

            // Analyze runtimes directory
            if (structure.HasRuntimesDir)
            {
                structure.RuntimeFiles.AddRange(Directory.GetFiles(runtimesDir, "*", SearchOption.AllDirectories));
            }

            // Analyze src directory
            if (structure.HasSrcDir)
            {
                structure.SourceFiles.AddRange(Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories));
            }

            return structure;
        }

        private PackageInfo? AnalyzeNuspecFile(string nuspecPath)
        {
            try
            {
                var doc = XDocument.Load(nuspecPath);
                var ns = doc.Root?.GetDefaultNamespace();
                var metadata = doc.Root?.Element(ns + "metadata");

                if (metadata == null) return null;

                return new PackageInfo
                {
                    Id = metadata.Element(ns + "id")?.Value ?? "",
                    Version = metadata.Element(ns + "version")?.Value ?? "",
                    Title = metadata.Element(ns + "title")?.Value ?? metadata.Element(ns + "id")?.Value ?? "",
                    Authors = metadata.Element(ns + "authors")?.Value ?? "",
                    Description = metadata.Element(ns + "description")?.Value ?? "",
                    ProjectUrl = metadata.Element(ns + "projectUrl")?.Value ?? "",
                    LicenseUrl = metadata.Element(ns + "licenseUrl")?.Value ?? "",
                    Tags = metadata.Element(ns + "tags")?.Value ?? ""
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not parse nuspec file {nuspecPath}: {ex.Message}");
                return null;
            }
        }

        private PackageInfo? AnalyzeProjectFile(string projectPath)
        {
            try
            {
                var doc = XDocument.Load(projectPath);
                var propertyGroups = doc.Descendants("PropertyGroup");

                var packageInfo = new PackageInfo();

                foreach (var group in propertyGroups)
                {
                    packageInfo.Id = group.Element("PackageId")?.Value ?? 
                                   group.Element("AssemblyName")?.Value ?? 
                                   Path.GetFileNameWithoutExtension(projectPath);
                    
                    packageInfo.Version = group.Element("Version")?.Value ?? 
                                        group.Element("AssemblyVersion")?.Value ?? "1.0.0";
                    
                    packageInfo.Title = group.Element("Title")?.Value ?? packageInfo.Id;
                    packageInfo.Authors = group.Element("Authors")?.Value ?? "Unknown";
                    packageInfo.Description = group.Element("Description")?.Value ?? "";
                    packageInfo.ProjectUrl = group.Element("PackageProjectUrl")?.Value ?? "";
                    packageInfo.LicenseUrl = group.Element("PackageLicenseUrl")?.Value ?? "";
                    packageInfo.Tags = group.Element("PackageTags")?.Value ?? "";
                }

                return packageInfo;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not parse project file {projectPath}: {ex.Message}");
                return null;
            }
        }
    }
}
