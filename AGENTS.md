# AGENTS.md — vvvv-mcp

MCP (Model Context Protocol) tooling for **vvvv gamma** (visual programming on .NET, .vl files).
Goal: a professional AI patching assistant that builds whole connected subgraphs in one call,
verified against the live vvvv instance.

> Terminology: "vvvv gamma" = current product. "VL" = its visual language. "vvvv beta" = the
> old product — never apply its concepts.

## Architecture (two processes)

1. **`src/VvvvMcp` + `src/VvvvMcp.Core`** — the MCP server (stdio, .NET 8 global tool).
   Used by MCP clients (Kilo/VS Code, Claude, Cursor, Open WebUI). Reads/writes .vl files
   directly (XML), talks to the bridge over HTTP when vvvv runs.
2. **`VL.MCP.HDE`** — vvvv editor extension (HDE package) hosting the bridge:
   `HttpListener` REST on `:7123` + MCP-over-SSE on `/sse` + chat host (Open WebUI, `:7125`).
   Toggle in vvvv menu: **Alt+B** (bridge), **Alt+C** (chat).

Key services (VvvvMcp.Core/Services):
- `PatchBuilderService` — `build_patch`: one-call subgraph builder (resolve → deps → pins →
  layout → links → save → reload → verify). THE primary write path.
- `NodeResolutionService` — live registry first (bridge `/api/nodes`), offline catalog fallback.
- `PatchWriterService` / `PatchReaderService` — .vl XML read/write.
- `SearchIndexService` — SQLite FTS5 (two-phase: AND then OR fallback). Schema version via
  `PRAGMA user_version` — bump `SchemaVersion` when FTS tables change.
- `BridgeClientService` — HTTP client for the bridge. Env override `VVVV_MCP_BRIDGE_PORT`.

Bridge (VL.MCP.HDE/src):
- `MCPBridgeServer.cs` — ProcessNode, REST routes, `IDisposable` (MUST release the listener
  on dispose or the port stays hostage across recompiles).
- `LiveNodeCatalog.cs` — live node snapshot from `NodeFactoryRegistry.Factories` (.NET nodes)
  + `LatestCompilation.DocumentsAndPackages → DefinedSymbols` (VL-defined nodes). Reflection
  over VL.Lang (no compile-time ref). Auto-rebuilds when the factory set changes.
- `BridgeState.cs` — documents/errors/packages via reflection. Errors carry
  `DocumentId`/`ElementId` (== .vl XML Id attributes) from `VL.Lang.Message.Location`.
- `McpChatHost.cs` — Open WebUI lifecycle: **named mutex `Global\vvvv-mcp-chat-start`**
  serializes startups across Alt+C presses AND HDE reloads; adopt healthy running instance
  (MCP-registration failure is non-fatal); never cancel/kill on disable (the "Open Chat"
  pin is a one-frame bang); server dies only on Dispose (vvvv exit). `/chat` 302-redirects
  to OWUI when up, else serves the placeholder page (polls same-origin `/api/chat/status`,
  reloads on ready — never navigate cross-origin from CEF client-side). Chat host sets
  `WEBUI_AUTH=False`/`ENABLE_SIGNUP=False` (else OWUI shows an admin-setup prompt).
  Loading `/chat` (a chat window opening) sets `_chatWanted` → auto-starts OWUI even
  without Alt+C (handles vvvv-restart-with-chat-open). OWUI console output: only error-ish
  lines forward to the vvvv console; all lines buffered → `GET /api/chat/log`.

## Ground-truth rules learned the hard way

- Node XML `Bounds` height is ALWAYS 19 (header only); width from name + visible pin rows.
- vvvv serializes ALL pins; hidden ones get `IsHidden="true"`: `Node Context`, **state
  outputs** (hide unless operating on the instance), optional-unlinked pins. Pin-group base
  pins (`Child`) are hidden in symbol data but their INSTANCES are visible — never hide them.
- Pin groups serialize as `Child`, `Child 2`, … (build_patch auto-indexes on repeat links).
- `LastDependency` (current) supersedes `LastSymbolSource` (legacy). Value = the .vl file
  actually defining the node (e.g. `VL.Stride.Runtime.vl`).
- NugetDependency elements are children of `Document`, conventionally AFTER `</Patch>`.
- Compile errors only exist for documents LOADED in the session — verify requires opening.
- External file edits do NOT refresh the vvvv UI — use bridge `/api/reload`
  (`Document.ReloadAsync`).
- Packages load lazily: a pack's nodes appear in the live registry only after a document
  referencing it is in the solution.
- **Live pin edit** (`set_value_live`): target the NODE's elementId + pin NAME, use
  `DevEnvHost.CurrentSolution` (NOT `SessionNodes.CurrentSolution` — active-canvas-scoped),
  `ReplaceDescendent` (close the generic) + `MakeCurrent(CommitToValue | UpdateUIAndRuntime)`.
  `AffectCompilation` does NOT commit pin values.

## Build & test

```powershell
dotnet build src/VvvvMcp.sln                  # MCP server
dotnet build VL.MCP.HDE/src/VL.MCP.Bridge.csproj   # bridge (also hot-recompiled by vvvv when editable)
dotnet run --project src/VvvvMcp.Tests        # smoke tests incl. live build_patch benchmark
```

vvvv side: start vvvv with `--package-repositories "X:/_dev/vvvv-mcp/" --editable-packages VL.MCP.HDE`.
If the bridge was loaded as binary (lib/net8.0 dll), a vvvv restart is needed after
`dotnet build` of the bridge.

## Knowledge & data pipelines

- `knowledge/*.md` — auto-discovered by `KnowledgeService` (top-level only).
  Manually maintained: vl-quickref, vl-patterns, vl-building-blocks, vl-common-graphs,
  vl-project-architecture, vvvv-internals-advanced (registered in `scripts/build-knowledge.ps1`).
- `scripts/build-knowledge.ps1` — regenerates gray-book-*.md from the submodule.
- `scripts/describe-graybook-images.ps1` — PREFERRED image pipeline: local Ollama vision model
  (default qwen3-vl:8b) describes gray book images → `knowledge/gray-book-image-text.md`.
  Incremental + resumable + abort-safe; `-Model`, `-OllamaUrl`, `-TimeoutSec`, `-MaxImages` params.
  (supersedes `scripts/ocr-graybook-images.ps1` — Windows OCR quality is too poor for screenshots).
- `scripts/scrape-forum.ps1` — Discourse scrape → vl-forum-solutions.md / vl-forum-snippets.md
  (must run in Windows PowerShell 5.1-compatible syntax — no `?.`).
- `scripts/index-help-patches.ps1` — help-patch index from `packs-community/` (LOCAL ONLY,
  not git-tracked, never redistribute the packs).
- `VVVVNodeAnalyzer/` — offline catalog builder (vvvv_nodes_mcp.json). Known limits: misses
  VL-defined nodes and C# nodes in package DLLs; live registry is the better source.

## Licensing

Dual license (see LICENSE.md): PolyForm-Noncommercial for non-commercial use, paid commercial
licenses via polar.sh. Keep attribution/CC BY-SA for tebjan-derived knowledge files
(THIRD-PARTY-NOTICES.md). Never bundle VL.* assemblies or the packs-community folder.
