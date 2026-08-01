# VVVVNodeAnalyzer

Extracts a comprehensive, MCP-ready node catalog from vvvv gamma NuGet packages.
Produces `vvvv_nodes_mcp.json` — the node database used by the vvvv-mcp server.

**No local vvvv installation required.** All packages are available on NuGet.org.

---

## Quick Usage

### Regenerate the full catalog

```powershell
# From the repo root — downloads packages and rebuilds the catalog:
./scripts/update-catalog.ps1
```

### Analyze a single package

```powershell
dotnet run -- "C:\path\to\VL.SomePackage"
dotnet run -- "C:\path\to\VL.SomePackage" nodes-only
```

### Batch-analyze a directory of packages

```powershell
dotnet run -- batch "C:\path\to\packs-community" "output-dir"
```

---

## How It Works

### VL Node Types

vvvv gamma has four principal node kinds defined in `.vl` files:

| Type | VL meaning | State | Typical C# pattern |
|---|---|---|---|
| **Process** | Stateful, Create+Update+Dispose lifecycle | Yes | `[ProcessNode]` class |
| **Operation** | Pure function, no state | No | Static method |
| **Record** | Immutable value type; operations return new instances | No | `record` |
| **Class** | Mutable object type; operations modify the instance in-place | Yes | `class` |

The analyzer also handles `Interface` and `Forward` definition kinds (less common).

### VL Category Resolution

Categories in `.vl` files follow a **canvas name hierarchy** — the category path is built from the `Name` attributes of nested `<Canvas>` elements, not from a single `DefaultCategory` attribute:

```xml
<Canvas CanvasType="FullCategory">          <!-- root: no category -->
  <Canvas Name="3D">                        <!-- category: "3D" -->
    <Canvas Name="Transform">              <!-- category: "3D.Transform" -->
      <Node Name="TransformSRT" .../>      <!-- lives in "3D.Transform" -->
```

This is the primary source of category information. `DefaultCategory` attributes and `LastCategoryFullName` on `NodeReference` are used as fallbacks.

### Getter/Setter Synthesis

**Getter and setter nodes are synthesized — they do not appear in the `.vl` XML.**

For every `<Slot>` (field/property) in a Record or Class definition, two nodes are generated at runtime by vvvv:

- **Getter** `Name` → takes the type instance, outputs the field value
- **Setter** `Set Name` → takes the type instance + new value, outputs the (new) instance

The analyzer synthesizes these directly from `<Slot>` elements without requiring any matching method names. For Records (immutable) the setter returns a new instance; for Classes (mutable) it returns the same instance.

For **.NET assemblies**, each public `get`-accessor becomes a getter node and each `set`-accessor becomes a separate setter node — matching how vvvv surfaces .NET properties.

### VL Type Names

The analyzer maps .NET type names to their vvvv equivalents:

| .NET | vvvv |
|---|---|
| `System.Int32` | `Integer32` |
| `System.Single` | `Float32` |
| `System.Boolean` | `Boolean` |
| `System.Int64` | `Integer64` |
| `System.Double` | `Float64` |
| `IEnumerable<T>` | `Sequence<T>` |

### Tags

Tags in `.vl` files are **space-separated** lowercase terms (per vvvv Design Guidelines), e.g. `"math filter 2d"`. The analyzer splits on spaces, not commas.

### Version Parsing

Node names can include a version qualifier: `"Split (Count)"` → Name = `"Split"`, Version = `"Count"`. This distinguishes overloaded nodes in the node browser.

### Help File Exclusion

Files inside a `help/` subdirectory and files whose names start with `HowTo`, `Reference`, `Explanation`, `Tutorial`, or `Example` are excluded from analysis — they contain usage examples, not node definitions.

---

## Output Format

The catalog is written to `output/vvvv_nodes_mcp.json` (relative to the VVVVNodeAnalyzer directory):

```json
{
  "libraryName": "vvvv Gamma Core",
  "version": "",
  "description": "Merged node dictionary from all vvvv Gamma core packages.",
  "extractionDate": "2026-08-01T09:00:00",
  "totalNodes": 6415,
  "categories": ["2D", "2D.Transform", "3D", "3D.Transform", ...],
  "nodesByType": { "Operation": 1613, "Process": 1160, "Method": 1382, ... },
  "nodes": [
    {
      "name": "TransformSRT",
      "version": "",
      "category": "3D.Transform",
      "fullName": "3D.Transform.TransformSRT",
      "type": "Operation",
      "summary": "Returns the input matrix transformed by first scaling, then rotating and finally translating it.",
      "remarks": "",
      "tags": [],
      "isGeneric": true,
      "hasState": false,
      "source": "VL.CoreLib",
      "inputs": [
        { "name": "Input", "type": "Matrix", "defaultValue": "", "isOptional": false },
        { "name": "Scaling", "type": "Vector3", "defaultValue": "", "isOptional": false },
        { "name": "Rotation", "type": "Quaternion", "defaultValue": "", "isOptional": false },
        { "name": "Translation", "type": "Vector3", "defaultValue": "", "isOptional": false }
      ],
      "outputs": [
        { "name": "Output", "type": "Matrix" }
      ]
    }
  ]
}
```

---

## Single-Package Analysis

```powershell
dotnet run -- <plugin-directory> [output-format] [output-path]
```

| Format | Output |
|---|---|
| `json` (default) | `analysis.json` — full structural analysis |
| `markdown` | `analysis.md` — human-readable report |
| `both` | Both of the above |
| `nodes-only` | `usable-nodes.json` + `usable-nodes.md` — just the node catalog entries |

---

## Batch Analysis

```powershell
dotnet run -- batch <packs-directory> <output-directory>
```

- Analyzes every subdirectory of `<packs-directory>` as a separate package
- Skips `dependencies/` subdirectory automatically
- Skips `VL.HDE` and `*_HDE_*` packages (editor-internal nodes, not user API)
- Merges all results into `output/vvvv_nodes_mcp.json` inside `<output-directory>`

---

## Project Structure

```
VVVVNodeAnalyzer/
├── Analyzers/
│   ├── VLLibraryAnalyzer.cs      # Parses .vl XML → VLNodeDefinition
│   │                              # - Canvas Name hierarchy for categories
│   │                              # - AllDirectories scan, help/ excluded
│   │                              # - Version parsing: "Name (Version)"
│   │                              # - Tags split by space
│   │                              # - Getter/setter from Slots (no method lookup)
│   │                              # - Process: uses Update operation pins
│   ├── UsableNodeExtractor.cs    # VLNodeDefinition → UsableNode
│   │                              # - Synthesizes getter/setter from Slots
│   │                              # - IsOptional from pin visibility attribute
│   ├── DotNetLibraryAnalyzer.cs  # .dll reflection → nodes
│   │                              # - VL type names (Integer32, Float32, ...)
│   │                              # - Properties → separate getter + setter nodes
│   │                              # - [ProcessNode] attribute detection
│   ├── PluginAnalyzer.cs         # Top-level orchestrator
│   ├── HelpSystemAnalyzer.cs     # Help patch scanner
│   └── PackageAnalyzer.cs        # .nuspec / .csproj metadata reader
├── Models/
│   ├── VLNodeDefinition.cs       # VLNodeType enum, VLPin (IsHidden, IsOptional), VLSlot
│   ├── UsableNode.cs             # UsableNodeType, UsablePin (IsGeneric), Version
│   ├── VLDocument.cs             # Patches, NodeDefinitions, NugetDependencies
│   └── NodeSummary.cs            # Flat summary for reporting
├── Exporters/
│   ├── UsableNodesExporter.cs    # JSON + Markdown output
│   └── ...
├── Extensions/
│   └── AnalysisExtensions.cs     # Statistics helpers
└── Program.cs                    # CLI entry point (single + batch modes)
```

---

## Requirements

- .NET 8 SDK
- Read access to the package directories being analyzed
- No vvvv installation required
