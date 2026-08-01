# vvvv-mcp

A [Model Context Protocol](https://modelcontextprotocol.io/) (MCP) server for [vvvv gamma](https://vvvv.org) — giving AI agents deep knowledge of vvvv's node API, the ability to read and explain `.vl` patches, and access to the full official documentation.

Works **without a vvvv installation** — for searching nodes, explaining patches, and generating new patches. vvvv does not need to be installed or running.

---

## Install (end users)

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later.

```powershell
# Install globally
dotnet tool install -g vvvv-mcp

# Configure your MCP client (Claude Desktop, VS Code, Cursor) automatically
vvvv-mcp --setup
```

`--setup` detects which MCP clients are installed, writes their config files, and prints the config snippet for anything it can't auto-detect. Run it again after updating.

### Update

```powershell
dotnet tool update -g vvvv-mcp
vvvv-mcp --setup   # re-run if paths changed
```

---

## What it does

- **Search** 6,400+ vvvv nodes by name, category, or keyword across all core packages
- **Read and explain** `.vl` patch files — parse the dataflow graph and describe it in natural language
- **Access the full vvvv knowledge base** — the entire Gray Book, all of tebjan's agent skills, and a curated package reference, served as MCP resources
- **Generate** new `.vl` patches and custom C# nodes (prompt-guided) - (not yet implemented)

The MCP is completely independent of any vvvv installation. It works whether or not vvvv is installed, and is not tied to any specific vvvv version.

---

## Tools

### Node Catalog

| Tool | Description |
|---|---|
| `search_nodes` | Search by name, category, or keyword. Returns nodes ranked by relevance with pins, types, and summaries. |
| `get_node_details` | Full details for a node by exact name — all pins, types, defaults, source package. |
| `list_categories` | All category namespaces (e.g. `3D.Transform`, `Stride.Models`), optionally filtered by prefix. |
| `list_packages` | All packages in the catalog. |

### Knowledge Base

| Tool | Description |
|---|---|
| `list_knowledge` | List all 17 knowledge documents with descriptions. |
| `read_knowledge` | Read the full content of a knowledge document by name. |
| `search_knowledge` | Full-text search across all knowledge documents with snippet results. |

### Patch Tools

| Tool | Description |
|---|---|
| `read_patch` | Parse a `.vl` file and return the structured graph (nodes, pins, links, IOBoxes, dependencies). |
| `explain_patch` | Natural-language explanation of a parsed patch. |
| `list_patch_dependencies` | List NuGet dependencies declared in a `.vl` file. |
| `read_file` | Read any source file (`.vl`, `.cs`, `.sdsl`, `.hlsl`, `.json`, etc.). |
| `list_directory` | Browse project directory structure. |

---

## Resources

### Knowledge (`vvvv://knowledge/`)

| URI | Content |
|---|---|
| `vvvv://knowledge/quickref` | XML cheat sheet: NodeReference patterns, critical .vl rules, VL.CoreLib categories, Stride scene, topic index |
| `vvvv://knowledge/file-format` | Complete `.vl` XML format reference (from tebjan/vvvv-skills) |
| `vvvv://knowledge/fundamentals` | Live compilation model, frame-based execution, node categories |
| `vvvv://knowledge/patching` | Dataflow patterns, regions, channels, event handling, anti-patterns |
| `vvvv://knowledge/custom-nodes` | `[ProcessNode]` lifecycle, `Update()`, change detection, assembly import |
| `vvvv://knowledge/shaders` | SDSL TextureFX/DrawFX/ComputeFX, streams system, mixins, GPU patterns |
| `vvvv://knowledge/dotnet` | .csproj setup, NuGet packages, vector type interop, async, threading |
| `vvvv://knowledge/channels` | IChannelHub, `[CanBePublished]`, reactive subscriptions, bang channels |
| `vvvv://knowledge/spreads` | `Spread<T>`, `SpreadBuilder`, mapping, filtering, performance rules |
| `vvvv://knowledge/node-libraries` | Creating node libraries: ImportAsIs/Namespace/Type, service registration |
| `vvvv://knowledge/troubleshooting` | Common errors: pin order, missing ImportAsIs, shader mistakes, runtime issues |
| `vvvv://knowledge/packages` | 230 curated packages from Libraries.xml, organized by category |
| `vvvv://knowledge/gray-book/language` | Official Gray Book — VL language: nodes, patches, operations, regions, types |
| `vvvv://knowledge/gray-book/extending` | Official Gray Book — writing nodes, shaders, design guidelines, libraries |
| `vvvv://knowledge/gray-book/libraries` | Official Gray Book — VL.CoreLib, Stride, collections, reactive, serialization |
| `vvvv://knowledge/gray-book/hde` | Official Gray Book — editor GUI, node browser, debugging, NuGet management |
| `vvvv://knowledge/gray-book/best-practice` | Official Gray Book — video, deployment, version control, text rendering |
| `vvvv://knowledge/gray-book/getting-started` | Official Gray Book — intro for .NET devs, creative coders, beta users |

### Catalog (`vvvv://catalog/`)

| URI | Content | 
|---|---|
| `vvvv://catalog/stats` | Node count by type, packages, top categories |
| `vvvv://catalog/categories` | Full sorted category list |

---

## Prompts

| Prompt | Description |
|---|---|
| `explain_vl_patch` | Guided workflow for reading and explaining a `.vl` patch file |
| `create_vl_patch` | Step-by-step guidance for generating a new `.vl` patch from a description |
| `create_csharp_node` | Template and guidance for creating a custom C# node |

---

## Developer / contributor setup

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Build

```powershell
dotnet build src/VvvvMcp.sln
```

### Run locally (without installing as a global tool)

```powershell
dotnet run --project src/VvvvMcp -- --setup   # configure MCP clients to use this build
```

**Environment variables** (auto-detected when using `--setup`, or set manually):

| Variable | Description |
|---|---|
| `VVVV_MCP_CATALOG` | Path to `vvvv_nodes_mcp.json`. Auto-detected when not set. |
| `VVVV_MCP_KNOWLEDGE` | Path to the `knowledge/` directory. Auto-detected when not set. |

### Test with MCP Inspector

```bash
npx @modelcontextprotocol/inspector -- dotnet src/VvvvMcp/bin/Debug/net8.0/VvvvMcp.dll
```

### Publish a new version

```powershell
# 1. Test the package locally (optional)
./scripts/publish.ps1 -Version 0.3.0

# 2. Commit, tag, push — GitHub Actions publishes to NuGet.org automatically
git add -A && git commit -m "release 0.5.0"
git tag v0.5.0
git push --tags
```

---

## Repository Structure

```
vvvv-mcp/
│
├── src/                          # MCP server (.NET 8)
│   ├── VvvvMcp.sln
│   ├── VvvvMcp/                  # Server entry point
│   │   ├── Program.cs
│   │   ├── Tools/                # search_nodes, read_patch, read_knowledge, ...
│   │   ├── Resources/            # vvvv://knowledge/* and vvvv://catalog/*
│   │   └── Prompts/              # explain_vl_patch, create_vl_patch, ...
│   ├── VvvvMcp.Core/             # Shared library
│   │   ├── Models/               # NodeModels, PatchModels, CatalogModels
│   │   └── Services/
│   │       ├── NodeCatalogService    # Node search & indexing
│   │       ├── KnowledgeService      # Knowledge document loading & search
│   │       ├── PatchReaderService    # .vl XML parser
│   │       └── PatchExplainerService # Natural language patch descriptions
│   └── VvvvMcp.Tests/            # Smoke tests
│
├── VVVVNodeAnalyzer/             # Node catalog generator
│   ├── Analyzers/
│   │   ├── VLLibraryAnalyzer.cs  # Parses .vl XML → VLNodeDefinition
│   │   ├── UsableNodeExtractor.cs # VLNodeDefinition → UsableNode
│   │   ├── DotNetLibraryAnalyzer.cs # .dll reflection → nodes
│   │   └── PluginAnalyzer.cs     # Top-level orchestrator
│   ├── Models/                   # VLNodeDefinition, UsableNode, VLDocument
│   ├── Exporters/                # JSON + Markdown output
│   └── output/
│       ├── vvvv_nodes_mcp.json   # Generated catalog
│       └── vvvv_nodes_mcp.md
│
├── knowledge/                    # MCP knowledge base (677 KB, 24 files)
│   ├── The-Gray-Book/            # Git submodule: vvvv/The-Gray-Book
│   ├── tebjan-vvvv-skills/       # Git submodule: tebjan/vvvv-skills
│   ├── gray-book-*.md            # Generated: Gray Book by section
│   ├── vvvv-*.md / vl-*.md       # Generated: tebjan's skill files
│   ├── vl-quickref.md            # Manual: XML cheat sheet + topic index
│   ├── vvvv-packages.md          # Generated: from Libraries.xml
│   └── MANIFEST.md               # What was generated, from which sources
│
├── packs-community/              # Downloaded NuGet packages (gitignored)
│   └── VL.PackageName.x.y.z/    # Extracted nupkg — structure matches vvvv packs/
│
├── output/                       # Root-level output copy (optional, gitignored)
│   └── vvvv_nodes_mcp.json
│
├── scripts/
│   ├── build-knowledge.ps1       # Regenerate knowledge/ from submodules + GitHub
│   ├── install-community-packs.ps1 # Download all vvvv NuGet packages
│   └── update-catalog.ps1        # Full pipeline: download → analyze → output
│
└── .vscode/
    └── mcp.json                  # VS Code MCP client configuration
```

---

## Node Catalog

### Generation

The catalog is generated by `VVVVNodeAnalyzer` from the actual vvvv NuGet packages. **No local vvvv installation required** — all packages are available on NuGet.org.

```powershell
# Download all packages and regenerate the catalog:
./scripts/update-catalog.ps1

# Force re-download (e.g. after a new vvvv release):
./scripts/update-catalog.ps1 -Force
```

### Coverage

The current catalog (`VVVVNodeAnalyzer/output/vvvv_nodes_mcp.json`) covers **38 packages** with **~6,400 user-facing nodes** across **374 categories**:

| Package group | Packages | Notable nodes |
|---|---|---|
| Core | VL.CoreLib, VL.CoreLib.Windows | Math, Collections, Reactive, Animation, 3D, IO |
| 3D Rendering | VL.Stride, VL.Stride.Runtime, VL.Stride.TextureFX, VL.Stride.Windows | SceneWindow, RootScene, Entity, Models, Materials, Cameras |
| 2D Rendering | VL.Skia | Canvas, Layers, Shapes, Text, Images |
| GUI | VL.ImGui, VL.ImGui.Skia, VL.ImGui.Stride | Buttons, Sliders, TextInput, ColorPicker |
| Audio | VL.Audio, VL.Audio.UI | Buffer, BufferPlayer, DSP, AudioSignal |
| Networking | VL.IO.OSC, VL.IO.Midi, VL.IO.ArtNet, VL.IO.Redis, VL.IO.WebSocket, VL.IO.TUIO, VL.IO.OSCQuery | Send/Receive nodes for each protocol |
| Serialization | VL.Serialization.Raw, VL.Serialization.MessagePack, VL.Serialization.FSPickler | Binary/MessagePack/FSPickler serialization |
| Platform | VL.Core, VL.AppServices, VL.TPL.Dataflow, VL.Video | Core services, TPL Dataflow, video |

### Node types in the catalog

| Type | Count | Description |
|---|---|---|
| Operation | ~1,600 | Stateless pure functions (e.g. `+`, `TransformSRT`, `Lerp`) |
| Process | ~1,200 | Stateful nodes with Create+Update+Dispose lifecycle |
| Method | ~1,400 | Member operations on a type |
| Setter | ~1,000 | Synthesized from type fields/properties |
| Getter | ~1,000 | Synthesized from type fields/properties |
| Class | ~200 | Mutable object types |
| Record | ~90 | Immutable value types |

### Extending the catalog

To add community packages (VL.Fuse, VL.OpenCV, VL.MediaPipe, etc.):

```powershell
# Download ALL community packages from Libraries.xml + core packages:
./scripts/install-community-packs.ps1

# Then run analysis on the downloaded packages:
dotnet run --project VVVVNodeAnalyzer/VVVVNodeAnalyzer.csproj -- batch packs-community VVVVNodeAnalyzer
```

---

## Knowledge Base

The knowledge base lives in `knowledge/` and is served as MCP resources. It is built from two git submodules and one live-fetched source:

| Source | Content | Files |
|---|---|---|
| `knowledge/The-Gray-Book` (submodule) | Official vvvv documentation | `gray-book-*.md` (7 sections, ~420 KB) |
| `knowledge/tebjan-vvvv-skills` (submodule) | Expert agent skill files | `vvvv-*.md` / `vl-*.md` (14 files, ~200 KB) |
| Libraries.xml (fetched live) | Official package catalog | `vvvv-packages.md` |
| Manual | XML cheat sheet | `vl-quickref.md` |

**To regenerate after submodule updates:**

```powershell
git submodule update --remote --merge
./scripts/build-knowledge.ps1
```

The script regenerates all 22 generated files. The one manually maintained file (`vl-quickref.md`) is never overwritten.

**Images in the Gray Book:** The Gray Book contains ~239 diagrams and screenshots. In the generated markdown files, image references are replaced with `[IMAGE: filename -- "alt text"]` markers so the LLM knows visual content exists. Future work: use vision models to describe diagrams inline.

---

## Architecture

```
MCP Client (Claude / Cursor / Copilot)
         │  stdio JSON-RPC
         ▼
┌─────────────────────────────────────────┐
│            VvvvMcp (server)              │
│                                          │
│  Tools          Resources    Prompts     │
│  search_nodes   knowledge/*  explain_vl  │
│  read_patch     catalog/*    create_vl   │
│  read_knowledge              create_cs   │
│  ...                                     │
│                                          │
│  NodeCatalogService   (6,400 nodes)      │
│  KnowledgeService     (677 KB docs)      │
│  PatchReaderService   (.vl XML)          │
│  PatchExplainerService                   │
└─────────────────────────────────────────┘
         │                    │
         ▼                    ▼
vvvv_nodes_mcp.json       knowledge/*.md
(VVVVNodeAnalyzer/output/  (generated by
VVVVNodeAnalyzer)        build-knowledge.ps1)
```

---

## Roadmap

- **Phase 1** ✅ Read-only tools — node search, patch reading, natural language explanations
- **Phase 1.5** ✅ Knowledge base — full Gray Book, tebjan's skills, 677 KB of vvvv documentation
- **Phase 1.6** ✅ Analyzer improvements — correct VL category resolution, getter/setter synthesis, VL type names, version parsing, AllDirectories scan
- **Phase 2** 🔜 Write capabilities — create/edit `.vl` patches, generate C# plugins and SDSL shaders
- **Phase 3** 🔜 Live vvvv bridge — console output capture, rendering snapshots (Spout), hot-reload feedback loop
- **Phase 4** 🔜 Community ecosystem — analyze all Libraries.xml packages, index help patches as examples, per-node usage lookup
- **Phase 5** 🔜 Continuous improvement agents — forum/changelog scraper, broken-patch learner, knowledge updater

---

## License

MIT
