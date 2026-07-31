using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VvvvPluginAnalyzer.Models;

namespace VvvvPluginAnalyzer.Analyzers
{
    public class DirectoryAnalyzer
    {
        public DirectoryStructureInfo AnalyzeDirectory(string pluginDirectory)
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
                structure.LibFiles = Directory.GetFiles(libDir, "*", SearchOption.AllDirectories).ToList();
                structure.DotNetLibraries = Directory.GetFiles(libDir, "*.dll", SearchOption.AllDirectories).ToList();
            }

            // Analyze runtimes directory
            if (structure.HasRuntimesDir)
            {
                structure.RuntimeFiles = Directory.GetFiles(runtimesDir, "*", SearchOption.AllDirectories).ToList();
            }

            // Analyze src directory
            if (structure.HasSrcDir)
            {
                structure.SourceFiles = Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories).ToList();
            }

            return structure;
        }
    }
}