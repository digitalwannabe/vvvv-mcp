# vvvv Gamma Plugin Analyzer

A comprehensive tool for analyzing vvvv Gamma plugins, supporting both VL documents and .NET libraries.

## Features

### Plugin Type Detection
- **VL Document Plugins**: Traditional VL-based plugins with .vl files
- **.NET Library Plugins**: Pure .NET library packages that expose nodes through reflection
- **Hybrid Plugins**: Plugins containing both VL documents and .NET libraries

### Analysis Capabilities

#### VL Document Analysis
- Parse .vl files and extract patch structure
- Analyze nodes, pins, and connections
- Extract dependencies and references
- Identify custom operations and processes

#### .NET Library Analysis
- Scan .dll files in lib directories (including subdirectories)
- Extract public types, methods, and properties
- Generate node information from .NET APIs
- Parse XML documentation when available
- Identify extension methods and static operations

#### Package Analysis
- Parse .nuspec files for package metadata
- Extract NuGet dependencies
- Analyze directory structure
- Validate plugin structure

#### Help System Analysis
- Scan help directories for documentation
- Categorize help patches by type
- Parse Help.xml structure files

## Usage

```bash
# Basic analysis with JSON output
VvvvPluginAnalyzer.exe "C:\path\to\plugin"

# Generate markdown report
VvvvPluginAnalyzer.exe "C:\path\to\plugin" markdown

# Generate both JSON and markdown
VvvvPluginAnalyzer.exe "C:\path\to\plugin" both

# Specify custom output path
VvvvPluginAnalyzer.exe "C:\path\to\plugin" json "custom-analysis.json"
```

## Output Formats

### JSON Export
Comprehensive machine-readable analysis including:
- Package metadata
- All discovered nodes with full pin information
- .NET library details with type information
- Dependency graph
- Help system structure

### Markdown Report
Human-readable report with:
- Package overview
- Node categorization by source (VL vs .NET)
- Library documentation status
- Help system summary
- Directory structure validation

## Node Discovery

### From VL Documents
- Extracts nodes defined in patches
- Analyzes pin types and connections
- Identifies custom operations
- Maps dependencies to external libraries

### From .NET Libraries
- Scans all public types in referenced assemblies
- Converts static methods to nodes
- Converts instance methods to nodes (with instance input)
- Converts properties to getter/setter nodes
- Preserves XML documentation as node help

## Plugin Structure Validation

The analyzer validates plugin structure based on type:
- Ensures required files are present (.nuspec, .vl or .dll)
- Checks directory structure conventions
- Validates dependency declarations
- Reports structural issues

## Advanced Features

### Complexity Metrics
- Node count statistics
- Patch complexity analysis
- Dependency depth analysis
- Type usage statistics

### Extension Methods
```csharp
// Get nodes by category
var nodesByCategory = result.GetNodesByCategory();

// Find nodes with specific operations
var mathNodes = result.FindNodesWithOperation("Add");

// Get complexity metrics
var metrics = result.CalculateComplexityMetrics();

// Get libraries with documentation
var documentedLibs = result.GetLibrariesWithDocumentation();
```

## Examples

### Analyzing a VL Plugin
```bash
VvvvPluginAnalyzer.exe "C:\vvvv\packages\VL.Devices.Leap" both
```

### Analyzing a .NET Library Plugin
```bash
VvvvPluginAnalyzer.exe "C:\vvvv\packages\VL.Skia" markdown
```

### Analyzing a Hybrid Plugin
```bash
VvvvPluginAnalyzer.exe "C:\vvvv\packages\VL.OpenCV" json
```

## Requirements

- .NET 8.0 or later
- Read access to plugin directories
- For .NET library analysis: assemblies must be loadable in current context

## Limitations

- .NET library analysis requires assemblies to be compatible with the analyzer's runtime
- Some obfuscated or native libraries may not be analyzable
- XML documentation parsing is best-effort and may not capture all formatting