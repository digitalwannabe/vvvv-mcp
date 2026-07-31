# vvvv gamma — Core Concepts Reference
> Synthesized from the vvvv gray book and tebjan/vvvv-skills.
> This document is the primary knowledge source for the vvvv-mcp.

---

## What is vvvv gamma?

vvvv gamma (also called VL) is a **live visual programming environment for .NET 8**.
- Programs are built by connecting **nodes** with **links** on a visual canvas
- The environment runs continuously — edits take effect immediately, no build/restart
- `.vl` files are XML documents encoding the visual program
- C# code is compiled live by vvvv itself via Roslyn on every save
- Shader files (`.sdsl`) also live-reload on save
- vvvv targets Stride (3D engine) and the full .NET ecosystem

---

## 1. Document Structure

A vvvv project is one or more **`.vl` files** (XML-based, version-controlled).

| File type | Purpose |
|-----------|---------|
| `.vl` | vvvv gamma document — visual programs, type definitions |
| `.sdsl` | Stride shader language files (superset of HLSL) |
| `.cs` | C# source files for custom nodes |
| `.csproj` | .NET project — can be referenced live from a .vl file |

### .vl XML Structure

```
Document (root)
├── NugetDependency (0..n) — always direct child of Document, NOT Patch
└── Patch (top-level, exactly 1)
    ├── Canvas (FullCategory — root visual container)
    └── Node (Name="Application" — entry point)
        └── Patch (inner)
            ├── Canvas (Group — operational content)
            │   ├── Node (operation calls, regions)
            │   ├── Pad (IOBoxes — value displays/editors)
            │   └── ...
            ├── Patch (Name="Create")
            ├── Patch (Name="Update")
            ├── ProcessDefinition
            │   ├── Fragment → Create patch
            │   └── Fragment → Update patch
            └── Link (0..n)
```

**Key rules:**
- `xmlns:p="property"` must be on the `Document` element
- `Version="0.128"` always required
- `LanguageVersion` matches the installed vvvv version
- All IDs are 22-character base62-encoded GUIDs
- Almost every document needs `VL.CoreLib` NuGet dependency

---

## 2. Execution Model

- **Frame-based evaluation** at ~60 FPS
- **Dataflow**: data flows left-to-right, top-to-bottom through links
- **Strictly evaluated** — all connected nodes execute every frame
- Disconnected subgraphs are skipped entirely
- **Process nodes** maintain state between frames (Create → Update → Dispose lifecycle)
- **Operation nodes** are pure functions, no state

---

## 3. Node Types

| Type | C# Pattern | State | Description |
|------|-----------|-------|-------------|
| `Process` | `[ProcessNode]` class | Yes | Stateful, Create+Update+Dispose lifecycle |
| `Operation` | static method | No | Pure function, no state |
| `Class` | class | Yes | Object-oriented, can be instantiated |
| `Record` | record | No | Immutable value type |
| `Method` | instance method | — | Operation on an existing object |
| `Getter` | get property | — | Read a property |
| `Setter` | set property | — | Write a property |

### Process Nodes (stateful)
- Have **Create** (constructor), **Update** (per-frame), **Dispose** lifecycle
- Internal state persists between frames
- Written with `[ProcessNode]` attribute in C#
- Change detection is critical for performance — cache results, only recompute when inputs change

### Operation Nodes (stateless)
- Pure functions: same input → same output, no side effects
- Written as static C# methods (auto-discovered)
- No `[ProcessNode]` attribute needed

### Adaptive Nodes
- A single node that adapts its implementation to connected types
- Example: `+` works with float, int, Vector2, Vector3, string
- Resolved at link-time, not runtime

---

## 4. Pins, Pads (IOBoxes), Links

| Element | Description |
|---------|-------------|
| **Pin** | Input or output connection point on a node |
| **Pad / IOBox** | Visual editor/display for a single value (number, text, boolean, color, etc.) |
| **Link** | Connection between an output pin and an input pin — defines data flow |

### IOBox (Pad) XML
```xml
<Pad Id="..." Bounds="x,y,w,h" ShowValueBox="true" isIOBox="true" Value="3.14" Comment="label">
  <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
    <Choice Kind="TypeFlag" Name="Float32" />
  </p:TypeAnnotation>
</Pad>
```
Note: lowercase `i` in `isIOBox`. Common type names: `Boolean`, `Int32`, `Float32`, `Float64`, `String`, `Vector2`, `Vector3`, `Color4`.

### Link XML
```xml
<Link Id="..." Ids="outputPinId,inputPinId" />
```
**Source (output) first, target (input) second** — this is a common mistake.

---

## 5. Regions (Visual Control Flow)

Regions are visual constructs that create a new computational context:

| Region | C# Equivalent | Usage |
|--------|--------------|-------|
| **ForEach** | `foreach` | Iterate over Spread elements |
| **If** | `if / else` | Conditional execution (Then / Else sub-patches) |
| **Switch** | `switch` | Multi-branch selection |
| **Repeat** | `for` loop | Loop N times |
| **Accumulator** | `Aggregate` | Running aggregation / fold |
| **Cache** | memoization | Cache result when inputs unchanged |

### Region XML Pattern
```xml
<Node Bounds="100,200,400,300" Id="...">
  <p:NodeReference LastCategoryFullName="Primitive" LastDependency="Builtin">
    <Choice Kind="StatefulRegion" Name="Region (Stateful)" Fixed="true" />
    <CategoryReference Kind="Category" Name="Primitive" />
    <Choice Kind="ApplicationStatefulRegion" Name="If" />
  </p:NodeReference>
  ...
</Node>
```

---

## 6. Type System

- **Statically typed** with type inference
- First-class support for **mutable** and **immutable** datatypes
- **Generics** (parametric polymorphism with bounded quantification)
- **Interfaces** (subtype polymorphism)

### Primitive Types

| VL Name | C# Type |
|---------|---------|
| `Boolean` | `bool` |
| `Byte` | `byte` |
| `Int32` / `Integer32` | `int` |
| `Int64` / `Integer64` | `long` |
| `Float32` | `float` |
| `Float64` | `double` |
| `Char` | `char` |
| `String` | `string` |

### Spatial / Math Types (from Stride.Core.Mathematics)

| VL Name | C# Type |
|---------|---------|
| `Vector2` | `Stride.Core.Mathematics.Vector2` |
| `Vector3` | `Stride.Core.Mathematics.Vector3` |
| `Vector4` | `Stride.Core.Mathematics.Vector4` |
| `Matrix` | `Stride.Core.Mathematics.Matrix` |
| `Quaternion` | `Stride.Core.Mathematics.Quaternion` |
| `Color4` / `RGBA` | `Stride.Core.Mathematics.Color4` |

### Collections

| VL Name | C# Type | Notes |
|---------|---------|-------|
| `Spread<T>` | `ImmutableArray<T>`-like | Primary vvvv collection; immutable, cyclic indexing in patches |
| `Sequence<T>` | `IEnumerable<T>` | Lazy sequence |
| `Dictionary<K,V>` | `Dictionary<K,V>` | Mutable key-value store |
| `HashSet<T>` | `HashSet<T>` | Mutable set |

**Spread<T> rules:**
- Never null — use `Spread<T>.Empty`
- Build with `SpreadBuilder<T>`, then `.ToSpread()`
- Cyclic indexing in patches (index wraps around)
- Reference equality is safe for change detection (immutable)

---

## 7. NodeReference System (How Nodes Are Referenced in .vl XML)

Every `<Node>` element has a `<p:NodeReference>` that identifies which operation/definition it calls:

### Operation Call (stateless)
```xml
<p:NodeReference LastCategoryFullName="Math" LastDependency="VL.CoreLib.vl">
  <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
  <Choice Kind="OperationCallFlag" Name="+" />
</p:NodeReference>
```

### Process Node (stateful)
```xml
<p:NodeReference LastCategoryFullName="Stride.Models" LastDependency="VL.Stride.vl">
  <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
  <Choice Kind="ProcessAppFlag" Name="Box" />
</p:NodeReference>
```

### Type Definition (Application/Process container)
```xml
<p:NodeReference>
  <Choice Kind="ContainerDefinition" Name="Process" />
  <CategoryReference Kind="Category" Name="Primitive" />
</p:NodeReference>
```

**Key rule:** `OperationCallFlag` = stateless operation, `ProcessAppFlag` = stateful process node.

---

## 8. ProcessDefinition / Lifecycle

Process nodes in the main Application canvas use a Create + Update lifecycle:

```xml
<Patch Id="innerPatchId">
  <Canvas Id="..." CanvasType="Group" />
  <Patch Id="createId" Name="Create" />
  <Patch Id="updateId" Name="Update" />
  <ProcessDefinition Id="...">
    <Fragment Id="..." Patch="createId" Enabled="true" />
    <Fragment Id="..." Patch="updateId" Enabled="true" />
  </ProcessDefinition>
  <!-- Links here -->
</Patch>
```

---

## 9. Reactive Programming (Observables)

vvvv includes a reactive programming layer built on `System.Reactive` (Rx.NET):

- `IObservable<T>` — event stream
- `IChannel<T>` — observable value container (read/write)
- `Observable.EveryFrame` — per-frame observable
- Bang = one-frame true pulse
- Toggle = alternating true/false

Reactive nodes live in the `Reactive` category.

---

## 10. VL.CoreLib Categories (Built-in)

| Category | Content |
|----------|---------|
| `2D` | Vector2, Rectangle, Circle, 2D transforms, 2D math |
| `3D` | Vector3, Box, Sphere, 3D transforms, 3D math |
| `3D.Transform` | TransformSRT, Translate, Scale, Rotate, etc. |
| `Adaptive` | +, -, *, /, Length, Normalize — work on multiple types |
| `Animation` | LFO, Stopwatch, Damper, Oscillator (time-based) |
| `Collections` | Spread, Sequence, Dictionary, HashSet, Set/Get index |
| `Color` | RGBA, HSL/HSV conversions, Lerp |
| `Control` | FlipFlop, MonoFlop, Toggle, Bang, Changed |
| `IO` | Mouse, Keyboard, Touch, File I/O, Networking |
| `Math` | Abs, Clamp, Lerp, Map, Mod, Sin, Cos, etc. |
| `Primitive` | Bool, Int32, Float32, Float64, String, Char |
| `Reactive` | Observable nodes, IChannel, Rx operators |
| `System` | XML, JSON, DateTime, Serialization |
| `Text` | String operations |

---

## 11. NuGet Packages (Key Libraries)

| Package | Domain | Key nodes/categories |
|---------|--------|---------------------|
| `VL.CoreLib` | Core | All VL.CoreLib categories above |
| `VL.Stride` | 3D Rendering | Stride engine, SceneWindow, RootScene, Entity, Transformation, Materials, Camera, Lighting |
| `VL.Stride.Runtime` | Stride internals | Low-level Stride access for C# nodes |
| `VL.Fuse` | GPU Visual Programming | Shader composition nodes, SDSL in patches |
| `VL.Skia` | 2D Rendering | Skia-based 2D graphics, canvas, layers |
| `VL.ImGui` | GUI | Immediate-mode UI |
| `VL.Audio` | Audio | Buffer, BufferPlayer, AudioSignal, DSP |
| `VL.OpenCV` | Computer Vision | OpenCV nodes for image processing |
| `VL.IO.OSC` | Networking | OSC send/receive |
| `VL.IO.MIDI` | MIDI | MIDI input/output |
| `VL.IO.MQTT` | Networking | MQTT broker/client |
| `VL.Devices.Kinect2` | Hardware | Azure Kinect depth camera |
| `VL.Devices.LeapMotion` | Hardware | Ultraleap hand tracking |
| `VL.Animation` | Animation | Tweening, keyframes, timeline |
| `VL.Elementa` | GUI | Declarative UI system |

---

## 12. Stride 3D Scene Structure

The most common 3D app structure:
```
SceneWindow ← receives Entity tree
  └── RootScene
        └── Entity (3D object with components)
              ├── Transform (TransformSRT — position, rotation, scale)
              ├── Model (Box, Sphere, custom mesh...)
              └── Material
```

Key Stride node categories:
- `Stride` — SceneWindow, RootScene, ForwardRenderer
- `Stride.Models` — Box, Sphere, Cylinder, Plane, custom mesh
- `Stride.Materials` — material creation and assignment
- `Stride.Cameras` — cameras
- `Stride.Lights` — lighting

---

## 13. C# Custom Nodes Quick Reference

### Stateful Process Node
```csharp
using VL.Core;

[ProcessNode]
public class MyNode : IDisposable
{
    private float _last;
    private float _cached;

    public void Update(
        out float result,   // outputs FIRST
        float input = 0f)   // inputs with defaults AFTER
    {
        if (input != _last)
        {
            _cached = Compute(input);
            _last = input;
        }
        result = _cached;
    }

    public void Dispose() { /* cleanup */ }
}
```

### Stateless Operation
```csharp
public static class MathOps
{
    public static float Remap(float value, float inMin = 0f, float inMax = 1f,
                              float outMin = 0f, float outMax = 1f)
    {
        float t = (value - inMin) / (inMax - inMin);
        return outMin + t * (outMax - outMin);
    }
}
```

### .csproj for custom nodes
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="VL.Core" Version="2025.7.*" />
  </ItemGroup>
</Project>
```

---

## 14. Common Patching Anti-Patterns

1. **Circular dependencies** → use `FrameDelay` to break cycles
2. **Too many nodes in one patch** → extract into sub-patches
3. **Polling instead of reacting** → use `IChannel<T>` or observables
4. **Ignoring nil/empty Spread** → always handle empty spreads gracefully
5. **Not using change detection in C# nodes** → cache and recompute only on change
6. **Allocations in Update loop** → no `new`, no LINQ in hot paths

---

## 15. VL File ID Generation

IDs are 22-character base62 strings using `[0-9A-Za-z]`. Generate with:
```csharp
GUIDEncoders.GuidTobase62(Guid.NewGuid())
```
Or any random 22-char alphanumeric string — all IDs must be unique within the document.

---

## Resources

- **Gray Book (official docs):** https://thegraybook.vvvv.org/
- **vvvv forum:** https://forum.vvvv.org/
- **tebjan's skills:** https://github.com/tebjan/vvvv-skills
- **vvvv packs catalog:** https://vvvv.org/packs
- **Gray Book — Language:** https://thegraybook.vvvv.org/reference/language/language.html
- **Gray Book — Libraries:** https://thegraybook.vvvv.org/reference/libraries/overview.html
- **Gray Book — Writing Nodes:** https://thegraybook.vvvv.org/reference/extending/writing-nodes.html
