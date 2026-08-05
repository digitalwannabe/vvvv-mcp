# vvvv gamma — Big Project Architecture

> **Purpose:** how to scaffold professional multi-document vvvv projects.
> Distilled from two shipped production codebases: **VL.Helga** (media server / show control)
> and **vwgroup-medianight** (LED stage particle show with render node + control panel + foyer app).
> Read this when asked to create a PROJECT (not a single patch). For single patches use
> vl-common-graphs.md; for XML rules vl-building-blocks.md.

---

## 1. Repository layout (self-contained portable app)

Both projects converge on the same layout — the repo IS the deployable app:

```
<Project>/
├─ <App>.vl                     ★ thin entry doc at root (DefaultCategory stays "Main")
├─ <App>.Remote.vl / <App>_Control.vl   ← optional extra apps sharing the libs
├─ <App>.pc                     ← optional PublicChannels file (global hardware channels)
├─ start_<app>.bat              ← one launcher per entry app
├─ vvvv_download.bat            ← pins an EXACT teamcity build into vvvv/
├─ vvvv/                        ← pinned vvvv gamma runtime (git-ignored)
├─ nugets/ + package-repositories/    ← local NuGet cache + git-submodule packages
├─ vl/
│  ├─ <App>.Base.vl             ← THE fat library doc (all definitions, 100+)
│  ├─ <PRJ>.Common.vl / .Stage.vl / .Network.vl / .Utils.vl   ← or split by domain
│  ├─ <App>.Layer.vl            ← tiny doc per plugin contract (interfaces)
│  ├─ csharp/<Name>/<Name>.csproj     ← C# plugins (net8.0)
│  ├─ EditShaders/ + shaders/         ← Stride .sdsl project
│  └─ rnd/                      ← sketches/experiments (promote into libs later)
├─ content/  or  assets/        ← runtime data: autosave.xml, one XML per content item
└─ shows/                       ← venue/show setups
```

Launcher pattern (verbatim from both projects):

```bat
taskkill /f /im vvvv.exe
start "" "%~dp0vvvv\vvvv_gamma_<EXACT-VERSION>\vvvv.exe" ^
  --nuget-path "%~dp0nugets" --package-repositories "%~dp0package-repositories" ^
  --editable-packages VL.Fuse;VL.Spaghetti --open "%~dp0vl\<App>_Show_Render.vl"
```

Version pinning discipline: `LanguageVersion` in the .vl == downloaded runtime == csproj
package refs. git-ignore `/vvvv/`, `**/obj/`, `**/.vs/`, autosaves.

## 2. Document graph rules

1. **Entry docs are thin** (20–50 KB): just an Application patch instantiating the central
   app/context object and wiring top-level subsystem processes. Category stays `Main`.
2. **One fat library doc** carries ~all definitions (can be 5 MB, 65k lines, ~100 definitions),
   DefaultCategory = project name (`Helga`, `VWG-MN`).
3. **Split libraries by domain** with sub-categories: `VWG-MN.Common`, `VWG-MN.Stage`,
   `VWG-MN.Network`, `Helga.Layer`. (File name = category name, dot-separated.)
4. **Multiple entry docs share the same libs** — a remote/control panel is a separate
   document, not a flag: `Helga.vl` + `Helga.Remote.vl` → `Helga.Base.vl`.
5. References: project docs via `DocumentDependency Location="./vl/X.vl"`, C# via
   `ProjectDependency`, everything else via `NugetDependency`. Editable submodule packages
   show version `0.0.0`.
6. Naming: `<Project>.<Aspect>.vl` for libs, `<PRJ>_<App>.vl` for entries, PascalCase.

## 3. Definitions architecture (inside the fat doc)

Organize with banner comments (`<!-- ***** Section ***** -->`) and these suffix conventions
(observed verbatim in both projects):

| Suffix | VL kind | Role | Examples |
|---|---|---|---|
| `*Model` | **Record** | immutable domain state | `HelgaModel`, `VenueModel`, `MidiDeviceModel` |
| `*Settings` / `*Parameters` / `*Config` | **Record** | serializable options | `GlobalSettings`, `StageSettings`, `LEDConfig` |
| `*Runtime` | **Class** | mutable per-frame counterpart of a model | `HelgaRuntime`, `StageRuntime`, `ParticleRuntime` |
| `*App` / `Context` / `*Context` | **Class** | central root object | `HelgaApp`, `Context`, `FoyerContext` |
| `*UI` / `*Editor` / `*Browser` / `*View` | **Process** | ImGui/Skia UI | `LayerEditor`, `StageView (Skia)` |
| subsystem noun | **Process** | IO abstraction (takes app/context as input) | `Midi`, `Remoter`, `OutputManager` |
| `*Manager` / `*Controller` / `*Bridge` | Class/Process | subsystem services | `ParticleController`, `OSCBridge` |
| `I*` | **Interface** | plugin/content contract in its OWN tiny doc | `ILayer` (Helga.Layer.vl) |
| content names | **Class** | generative content implementing the interface | `FollowMe`, `Fireworks` |
| `SaveLoad*` / `*Undo` | **Process** | persistence/history | `SaveLoadGeneric`, `ModelUndo` |

## 4. State management — two proven flavors

### Flavor 1: Model-Runtime-Editor (Helga) — for document-centric tools

- One immutable `*Model` record tree = the whole document
  (`HelgaModel`: CurrentShow, ShowLibrary, LayerLibrary, …).
- Central `*App` class holds exactly: `Channel<Model>` + `*Runtime` instance.
  Operations: `ModelChannel()`, `Runtime()`, `Model()`.
- UI edits = read channel → build new record → `EnsureValue` back into the channel.
  Drill in with `Select (ByPath) "CurrentShow"`.
- Undo, autosave, remote-sync all hang off the model channel.
- Serialization: FSPickler XML, one file per content item in `content/`, timer autosave
  (`OnOpen + LFO(60s) + FileExists → SaveLoadGeneric`).

### Flavor 2: Context object (medianight) — for render installations

- Central `Context` class with a **slot per subsystem** (settings records, controllers,
  bridges) and `GetX`/`SetX` operations.
- Created once in Application, passed **explicitly as a `Context` input pin** to every
  process that needs it (`ParticleRuntime` takes `Context` + `StageRuntime`).
- Settings records serialized to `assets/*.xml`; live show control streams in via OSC →
  `OSCBridge` → channels.

### Both

- Global hardware channels via a `.pc` PublicChannels file (Helga: MIDI faders) or an
  OSC→channel bridge (medianight: Chataigne as external sequencer).
- **Records** for anything serializable, **classes** for anything stateful, **processes**
  for anything that draws or talks.

## 5. C# interop in big projects

- Small, focused plugins: `vl/csharp/<Name>/<Name>.csproj` (net8.0, refs `VL.Core` +
  engine package), referenced via `ProjectDependency` → hot-reload on save.
- Used for: scene/entity inspectors (Helga SceneEditor), algorithmic helpers
  (medianight: `SmallestEnclosingCircle`, `SecondsToTimecode`).
- Stride shaders: `vl/shaders/*.sdsl` + `EditShaders.csproj` globbing them.

## 6. Scaffolding checklist

1. Pin an exact gamma build; commit `vvvv_download.bat` + launcher bats.
2. Entry doc at root, libs in `vl/`, C# in `vl/csharp/<Name>/`, shaders in `vl/shaders/`.
3. Create the **model records first**, then mirroring **runtime classes**, then the
   **`*App`/`Context`** class, then UI/IO processes that take it as input.
4. Plugin contracts (interfaces) in their own tiny document with a sub-category.
5. Keep an `rnd/` sandbox with its own launcher; promote sketches into the lib docs.
6. Externalize show data: `content/`/`assets/` for serialized XML, `shows/` for venues,
   external sequencer (Chataigne/OSC) for timelines.
7. Tests = sandbox patches with `Tester` nodes; no formal framework needed.
