# vvvv Internals — ADVANCED (bridge, reflection, live session)

> **ADVANCED.** Internal structure of a running vvvv gamma instance as discovered via the
> VL.MCP.HDE bridge. Only read this when the standard tools are insufficient — e.g. you need
> live session data, deep error info, or want to extend the bridge. Everything here is
> reflection-based and may break between vvvv versions (verified against 2025.7.x / 7.4).

---

## 1. The bridge (VL.MCP.HDE)

An HDE editor extension (ProcessNode `MCPBridgeServer`) hosting an `HttpListener`:
- REST on `http://localhost:7123/api/*` (env override: `VVVV_MCP_BRIDGE_PORT`)
- MCP over SSE on `/sse` + `/mcp/message`
- Chat host (Open WebUI) on port 7125 (env override: `VVVV_MCP_CHAT_PORT`)

Toggle via vvvv menu: **Alt+B** (bridge), **Alt+C** (chat).

### REST endpoints

| Endpoint | Purpose |
|---|---|
| `GET /api/ping` | status + bridge version |
| `GET /api/state` | IsRunning/IsPaused/FrameCount/Uptime |
| `GET /api/documents` | open documents (name, filePath, isSaved, isChanged) |
| `GET /api/errors` | compile+runtime messages with **documentId + elementId** (see §3) |
| `GET /api/packages` | loaded VL packages (id, version, source binary/source) |
| `GET /api/channels` | global channels (stub) |
| `POST /api/documents/open` | LoadDocumentInBackground + ShowDocument (adds to solution, shows tab) |
| `POST /api/documents/new` | session.NewDocumentAsync |
| `POST /api/documents/close` `{filePath, save}` | close |
| `POST /api/documents/save[-all]` | save |
| `POST /api/reload` `{filePath}` | **Document.ReloadAsync** — reloads open doc from disk, updates UI (touch fallback if not open) |
| `GET /api/tabs`, `POST /api/tabs/close` | editor tabs |
| `POST /api/undo`, `/api/redo` | per active canvas |
| `GET /api/log?limit&severity` | captured vvvv console log |
| **`GET /api/nodes?query&category&limit&pins=1&refresh=1`** | live node search (see §4) |
| **`GET /api/nodes/lookup?name&category`** | exact node with full pin details |
| `GET /api/nodes/categories?prefix` | live category list |
| `GET /api/nodes/stats` | node count, build time, per-factory/per-source diagnostics |
| `GET /api/debug/explore?path=A.B[2]&methods=true&take=n` | reflection explorer from VLSession.Instance (see §5) |
| `GET /api/debug/mainform` | WinForms control tree |

## 2. Session object model (VL.Lang)

Root: `VL.Model.VLSession.Instance` (static). Concrete type at runtime: `VL.UI.Forms.WinFormsSession`.

Key properties:
- `CurrentSolution : VL.Model.Solution` → `Documents : IEnumerable<Document>` (open docs)
- `LatestCompilation : PreCompilation` — immutable compilation snapshot
  - `DocumentSymbols` — DocSymbols of OPEN/EDITABLE documents only
  - **`DocumentsAndPackages : IEnumerable<ISymbolSource>`** — DocSymbols + CompiledSymbols
    of every referenced package (.vl) — THE complete symbol source
- `NodeFactoryRegistry : NodeFactoryRegistry` — .NET-backed node factories (lazy!)
  - `Factories : IEnumerable<IVLNodeDescriptionFactory>` — grows as packages load
- `AvailableNugets : IEnumerable<PackageInfo>` — `Id, Version, PackagePath, IsVLPackage, IsHDEPackage, IsSourcePackage`
- `UserRuntime.Timing.FrameCount`, `LatestMessagesFromCompiler : IChannel<ImmutableHashSet<Message>>`
- `AppHost.SynchronizationContext` — marshal here for main-thread-only VL model APIs

### Laziness (important)

Packages load ON DEMAND: a package's factories and symbols appear only after a document
referencing it is part of the solution. The live node catalog therefore reflects
"nodes placeable in the current session". To make a package's nodes available:
add the NugetDependency to a document and open it.

## 3. Error/message model

`VL.Lang.Message` (VL.Core) — public FIELDS (not properties):
- `What, Why, How : string` (What=short, Why=explanation, How=fix hint)
- `Severity : MessageSeverity` (None/Info/Warning/Error/Critical)
- `Location : UniqueId` with **`DocumentId : string`** (== the .vl `<Document Id>`) and
  **`ElementId : string`** (== the node/pin `Id` in the XML) — exact error→node mapping
- `Source : LogSource`, `Symbol : object`

`VL.Lang.Symbols.DetailedMessage` additionally carries `DocumentPath`, `DefinitionName`.

## 4. Live node catalog (how the bridge enumerates nodes)

Two sources, merged by FullName (richest pin set wins):

1. **NodeFactoryRegistry.Factories → NodeDescriptions** (`IVLNodeDescription`):
   .NET-backed nodes. `Name, Category, FilePath, Fragmented, Inputs/Outputs : IVLPinDescription`
   with `Name, Type : System.Type` (real types!), `DefaultValue, PinGroupKind`.
   Stateful detection: presence of hidden `Node Context` input pin.

2. **LatestCompilation.DocumentsAndPackages → DefinedSymbols** (ILookup of
   ICategorizableSymbol): VL-defined nodes (processes, classes, records, operations).
   - Node defs implement `INodeDefinitionSymbol`: `Kind` (ElementKind), `Inputs/Outputs : IPinSymbol`,
     `IsGeneric`, `ContainingType`, `OperationDefinitions` (member ops of types)
   - `ICategorizableSymbol`: `Name : NameAndVersion` (`FullName` includes "(Variant)"),
     `ParentCategory`, `FilePath`, `Smell` (skip "Internal")
   - `IPinSymbol`/`IPinDefinitionSymbol`: `Type : ITypeSymbol`, `DefaultValue : CompileTimeValue`
     (unwrap `.Value`), `Visibility : PinVisibility` (Visible/Optional/Hidden),
     `PinGroupKind`, `IsState` (state in/out pins), name via `Definition.Element.Name`

Pin visibility semantics (match vvvv serialization):
- `Visibility=Hidden` → `IsHidden="true"` — EXCEPT pin-group base pins (instances stay visible)
- `Visibility=Optional` → hidden when unlinked and unvalued
- state outputs (`IsState`, "State Output", or output type == node type) → hidden unless linked

## 5. Reflection explorer (`/api/debug/explore`)

`?path=Foo.Bar[2].Baz` navigates properties from VLSession.Instance; `[n]` indexes into
enumerables; `&methods=true` lists methods; `&take=n` extends item listing (default 10);
`&interfaces=true`. Examples:

```
/api/debug/explore?path=&methods=true                                  → session members
/api/debug/explore?path=NodeFactoryRegistry.Factories&take=30          → registered factories
/api/debug/explore?path=LatestCompilation.DocumentsAndPackages[1]      → a package's CompiledSymbols
/api/debug/explore?path=CurrentSolution.Documents                      → open documents
```

## 6. Hot-reload behavior (verified)

- Editing a .vl on disk does NOT auto-refresh an open document — use `POST /api/reload`
  (Document.ReloadAsync) after external writes.
- Saving a .cs of an editable package's referenced .csproj recompiles via Roslyn and
  recreates node instances — the bridge implements `IDisposable` to release port 7123
  on recreation (without it the old listener holds the port forever).
- `Document.ReloadAsync(returnSelfIfFileWasDeleted: true)` is the official reload API.
- `SolutionUpdateKind` (AffectCompilation, UpdateUIOnly, …) drives `session.UpdateAsync`
  for model-level edits — not needed for file-based edits.

## 7. Chat host lifecycle (Alt+C)

- Open WebUI started via `uv run open-webui serve`, data dir `%LOCALAPPDATA%\vvvv-mcp\open-webui-data`.
- On enable: **adopt** a healthy already-running instance on the port (no restart);
  only kill port occupants that look like stale python/uv leftovers.
- Toggle off closes only the window — the server lives until vvvv exits (Dispose).
- MCP registration: POST `/openai/mcp/add` with the in-vvvv SSE URL.
