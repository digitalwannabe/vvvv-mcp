# vvvv gamma — Quick Reference Cheat Sheet

> **Orientation document.** This is the entry point. For authoritative detail, use the knowledge
> files listed in the "Where to look" section below.
> Last human review: 2026-07. Do NOT auto-overwrite.

---

## What is vvvv gamma?

vvvv gamma (file extension `.vl`) is a **live visual dataflow programming environment for .NET 8**.
Programs run continuously at ~60 FPS. Nodes are connected with links on a visual canvas.
C# compiles live via Roslyn. SDSL shaders live-reload on save. No restart needed for patch edits.

---

## NodeReference XML — the most important pattern to get right

Every `<Node>` in a .vl file has a `<p:NodeReference>` that identifies what it calls.
The **second Choice** determines whether it's stateless or stateful:

| Second `<Choice>` Kind | Meaning | Example node |
|------------------------|---------|--------------|
| `OperationCallFlag` | Stateless operation — pure function | `+`, `TransformSRT`, `Vector (Join)` |
| `ProcessAppFlag` | Stateful process node — Create+Update+Dispose | `Box`, `RootScene`, `SceneWindow`, `LFO` |
| `ContainerDefinition` | Type definition (Application/Process) | The Application node itself |
| `StatefulRegion` | Region (ForEach, If, Switch, Cache...) | `If`, `ForEach` |

**Stateless operation:**
```xml
<p:NodeReference LastCategoryFullName="Math" LastDependency="VL.CoreLib.vl">
  <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
  <Choice Kind="OperationCallFlag" Name="+" />
</p:NodeReference>
```

**Stateful process node:**
```xml
<p:NodeReference LastCategoryFullName="Stride.Models" LastDependency="VL.Stride.Runtime.vl">
  <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
  <Choice Kind="ProcessAppFlag" Name="Box" />
</p:NodeReference>
```

(`LastDependency` = the .vl file that actually defines the node, e.g. `VL.Stride.Runtime.vl` —
the live tools resolve this automatically. Variant names go into the Name: `Rotation (Successive)`.)

**Application (entry point) container:**
```xml
<p:NodeReference>
  <Choice Kind="ContainerDefinition" Name="Process" />
  <CategoryReference Kind="Category" Name="Primitive" />
</p:NodeReference>
```

---

## .vl XML Critical Rules (things that silently break patches)

1. `xmlns:p="property"` must be on `<Document>` — without it, nothing works
2. `Version="0.128"` always required on `<Document>`
3. `NugetDependency` is a child of `<Document>`, **NOT** inside `<Patch>`
4. `<Link Ids="outputPinId,inputPinId" />` — **output/source pin FIRST**
5. `isIOBox="true"` uses lowercase `i` — `IsIOBox` is wrong
6. `CanvasType="FullCategory"` only on the root canvas; inner canvases use `"Group"`
7. All IDs must be 22-character base62 strings — generate with `GUIDEncoders.GuidTobase62(Guid.NewGuid())`
8. Fragment `Patch` attribute must reference the **Id** of a sibling `<Patch>` element

---

## ProcessDefinition / Lifecycle skeleton

```xml
<Patch Id="...inner...">
  <Canvas Id="..." CanvasType="Group" />
  <Patch Id="...create..." Name="Create" />
  <Patch Id="...update..." Name="Update" />
  <ProcessDefinition Id="...">
    <Fragment Id="..." Patch="...create..." Enabled="true" />
    <Fragment Id="..." Patch="...update..." Enabled="true" />
  </ProcessDefinition>
  <!-- Link elements go here, INSIDE the inner Patch -->
</Patch>
```

---

## IOBox (Pad) for common types

```xml
<!-- Float32 -->
<Pad Id="..." Bounds="200,160,80,20" ShowValueBox="true" isIOBox="true" Value="1.0" Comment="My Label">
  <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
    <Choice Kind="TypeFlag" Name="Float32" />
  </p:TypeAnnotation>
</Pad>
```

Common `TypeFlag` names: `Boolean`, `Int32`, `Float32`, `Float64`, `String`, `Vector2`, `Vector3`, `Color4`

---

## VL.CoreLib — Category quick lookup

| Category | Key nodes |
|----------|-----------|
| `2D` | Vector2, Rectangle, Circle, 2D transforms |
| `3D` | Vector3, Box (geometry), Sphere, 3D transforms |
| `3D.Transform` | TransformSRT, Translate, Scale, Rotate |
| `3D.Vector3` | Vector (Join), Split, Normalize, Length, Dot, Cross |
| `Adaptive` | +, -, *, /, Length, Normalize — work on int/float/Vector2/3/4/string |
| `Animation` | LFO, Stopwatch, Damper, Oscillator |
| `Animation.FrameBased` | Frame-based versions of the above |
| `Collections` | GetSlice, SetIndex, Spread (Join), Cons, Zip, Sort, Count |
| `Color` | RGBA, ColorLerp, HSL/HSV conversions |
| `Control` | FlipFlop, MonoFlop, Toggle, Bang, Changed, FrameDelay, S+H |
| `IO` | Mouse, Keyboard, Touch, File read/write, Path |
| `Math` | Abs, Clamp, Lerp, Map, Mod, Sin, Cos, Pow, Sqrt, Floor, Round |
| `Primitive` | Bool, Byte, Int32, Int64, Float32, Float64, Char, String |
| `Reactive` | Observable nodes, IChannel, Rx operators |
| `System` | XML, JSON, DateTime, Serialization, Environment |

---

## Stride 3D scene — typical structure

```
SceneWindow [Stride]            ← entry point for 3D rendering
  └── RootScene [Stride]        ← .Output → SceneWindow.Input
        ├── Box/Sphere/Plane [Stride.Models]   ← .Entity → RootScene.Child (pin group!)
        │     └── PBRMaterial (Metallic) [Stride.Materials] → .Material
        ├── DirectionalLight / SkyboxLight [Stride.Lights]  ← .Entity → RootScene.Child
        └── OrbitCamera [Stride.Cameras] → SceneWindow.Camera
```

Rotation over time: `Rotation (Successive) [3D.Transform]` — feed `Angular Delta`
(Vector3, cycles per FRAME, e.g. `-0.02, 0, 0`), output `Result` is a Matrix →
connect directly to a model's `Transformation` pin (no TransformSRT needed).

Key package: `VL.Stride` → NugetDependency `Location="VL.Stride"` (resolves to VL.Stride.Runtime)
Key categories: `Stride` (SceneWindow, RootScene), `Stride.Models`, `Stride.Materials`, `Stride.Cameras`, `Stride.Lights`
More pin-level graphs: `vl-common-graphs`.

---

## Where to look for deeper information

| Topic | Knowledge file | MCP resource |
|-------|---------------|--------------|
| **Building blocks (definitions, regions, pads, XML)** | `vl-building-blocks` | — |
| **Common graphs (pin-level patterns)** | `vl-common-graphs` | — |
| **Big project scaffolding** | `vl-project-architecture` | — |
| VL language (nodes, patches, regions, types) | `gray-book-language` | `vvvv://knowledge/gray-book/language` |
| .vl XML format (complete reference) | `vl-file-format` | `vvvv://knowledge/file-format` |
| Libraries (CoreLib, Stride, collections, reactive) | `gray-book-libraries` | `vvvv://knowledge/gray-book/libraries` |
| Patching patterns, dataflow, anti-patterns | `vvvv-patching` | `vvvv://knowledge/patching` |
| Writing C# nodes (ProcessNode, Update, pins) | `vvvv-custom-nodes` | `vvvv://knowledge/custom-nodes` |
| SDSL shaders (TextureFX, DrawFX, compute) | `vvvv-shaders` | `vvvv://knowledge/shaders` |
| Extending vvvv, node libraries, contributing | `gray-book-extending` | `vvvv://knowledge/gray-book/extending` |
| All available packages (curated list) | `vvvv-packages` | `vvvv://knowledge/packages` |
| Spread<T>, SpreadBuilder | `vvvv-spreads` | `vvvv://knowledge/spreads` |
| Channels, IChannelHub | `vvvv-channels` | `vvvv://knowledge/channels` |
| .NET integration, .csproj, NuGet | `vvvv-dotnet` | `vvvv://knowledge/dotnet` |
| Troubleshooting, common errors | `vvvv-troubleshooting` | `vvvv://knowledge/troubleshooting` |
| Getting started, intro for .NET devs | `gray-book-getting-started` | `vvvv://knowledge/gray-book/getting-started` |
| HDE (editor, node browser, debugging) | `gray-book-hde` | `vvvv://knowledge/gray-book/hde` |
| **vvvv internals (bridge, reflection, live session) — ADVANCED** | `vvvv-internals-advanced` | — |
