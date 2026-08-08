# VL Building Blocks — Document & Definition Model

> **Purpose:** authoritative reference for the structural elements of a .vl document —
> application, definitions, processes, classes, records, interfaces, operations, pads,
> regions, channels, delegates, and how they serialize to XML.
> Ground truth: `knowledge/templates/vl/basic_vl_objects.vl` + The Gray Book (reference/language).
> Read this BEFORE creating documents with definitions (not needed for flat application patches).
> Terminology: "vvvv gamma" = the current product (file extension .vl); "VL" = the visual
> language you program in (node-based .NET). "vvvv beta" = the old product — never use its concepts.

---

## 1. Document model

A `.vl` file is XML. One document = one top-level `Patch` containing the **definitions patch**
(root canvas) + the **Application node** (entry point). If the Application patch is empty,
the document is a **library** (only provides definitions to referencing documents).

```xml
<?xml version="1.0" encoding="utf-8"?>
<Document xmlns:p="property" xmlns:r="reflection" Id="O1Gy197KU9tOxKhf68T24u" LanguageVersion="2025.7.4" Version="0.128">
  <Patch Id="UIBtS9aVYzuOm2VpsNAeY4">
    <Canvas Id="RqB3RGDRaAyLZPBuXWMisn" DefaultCategory="Main" CanvasType="FullCategory" />
    <!-- definitions + Application node live here -->
  </Patch>
  <NugetDependency Id="Cqx9AU6le7NObkHDDtOXjq" Location="VL.CoreLib" Version="2025.7.4" />
</Document>
```

- `Id`: 22-char base62 GUID, unique per element. `Version` is always `"0.128"`.
- `xmlns:p="property"` required; `xmlns:r="reflection"` only for explicit nulls.
- **Dependencies are children of `Document`, conventionally AFTER `</Patch>`** (that's how vvvv saves).
- Dependency kinds:
  - `NugetDependency Location="VL.Stride"` — VL or .NET NuGet packages
  - `DocumentDependency Location="./Other.vl"` / file references — sibling .vl files must be
    referenced explicitly to be visible (NO automatic same-directory visibility)
  - `ProjectDependency` — .csproj references (compiled live via Roslyn, hot-reload on save)
- `IsForward="true"` on a dependency re-exports its nodes to consumers of this document.
- Packages are read-only at runtime unless started with `--editable-packages`.

## 2. Application vs Definitions

- **Application patch** (Alt+A): the program entry point; a special Process patch with
  Create+Update only. Executes as soon as the document is opened (directly or as dependency).
- **Definitions patch** (Alt+Shift+A): root of all type definitions, static operations,
  categories, groups. Root canvas carries `DefaultCategory="Main" CanvasType="FullCategory"`.
- **Categories = namespaces.** Nested category canvases append with dots. Groups structure
  visually without affecting the category.

Application node XML:

```xml
<Node Name="Application" Bounds="100,100" Id="...">
  <p:NodeReference>
    <Choice Kind="ContainerDefinition" Name="Process" />
    <CategoryReference Kind="Category" Name="Primitive" />
  </p:NodeReference>
  <Patch Id="...">
    <Canvas Id="..." CanvasType="Group"> <!-- application content here --> </Canvas>
    <Patch Id="createId" Name="Create" />
    <Patch Id="updateId" Name="Update" />
    <ProcessDefinition Id="...">
      <Fragment Id="..." Patch="createId" Enabled="true" />
      <Fragment Id="..." Patch="updateId" Enabled="true" />
    </ProcessDefinition>
    <Link Id="..." Ids="sourcePinId,targetPinId" />  <!-- links are children of the inner Patch -->
  </Patch>
</Node>
```

## 3. Definitions (the VL type system)

Datatype patch kinds: **Process, Record, Class, Interface, Forward**. Created in the
definitions patch; each is a `Node` whose first Choice is the definition kind.

### Process — `Choice Kind="ContainerDefinition"` (no Name on Choice; name on the Node)

Stateful node type. Runs Create once, Update every frame, holds state in pads between frames.
This is THE most common definition kind — every non-trivial patch defines processes.

```xml
<Node Name="Process" Bounds="703,410" Id="...">
  <p:NodeReference><Choice Kind="ContainerDefinition" /></p:NodeReference>
  <Patch Id="...">
    <Canvas Id="..." CanvasType="Group" BordersChecked="false" />
    <Patch Id="cId" Name="Create" />
    <Patch Id="uId" Name="Update" />
    <ProcessDefinition Id="...">
      <Fragment Id="..." Patch="cId" Enabled="true" />
      <Fragment Id="..." Patch="uId" Enabled="true" />
    </ProcessDefinition>
  </Patch>
</Node>
```

### Record — `Choice Kind="RecordDefinition"` (immutable)

Operations return NEW instances; modified records must be written back into a Pad to survive
to the next frame. Ideal for dataflow + cross-thread data. Has a **hidden** ProcessDefinition
(`IsHidden="true"` on the ProcessDefinition element) with a Create fragment.

### Class — `Choice Kind="ClassDefinition"` (mutable)

Operations modify the original instance; links transport references. Chain writes in series
(multiple unordered writes = "yellow socks" warning). Same XML shape as Record.

### Interface — `Choice Kind="InterfaceDefinition"` (not officially supported yet)

Hidden ProcessDefinition with ZERO fragments.

### Operation (static) — `Choice Kind="OperationDefinition"`

Stateless function. Empty inner `<Patch Id="..." />` initially. Enable `Is Generic` to allow
generic inputs. Can get an **Apply** input (Configure) when first input/output share a type —
Apply=false bypasses the node (shortcut for an If region).

### Member operations & fragments

- Reserved link colors: white=Create, gray=Update, dark red=Dispose.
- Assignment propagates through a patch, stopping at Pads and Process Nodes.
- Elements with no assignment fall back to Update.
- `Dispose`: name a member operation "Dispose"; runtime calls it when the node is deleted.
- Pin groups only on pins annotated `Spread<T>`, `Array<T>`, `MutableArray<T>`, `Dictionary`, `MutableDictionary`.

## 4. Pads & IOBoxes

Same element (`<Pad>`): IOBox = Pad with `ShowValueBox="true" isIOBox="true"` (lowercase i!).
Pads reference **Properties** of the enclosing datatype by name (case-sensitive); all same-named
pads share one property. Per operation: pads are read first, written last. Link in from above =
write; link out at bottom = read. Anonymous pads (no name) = link hubs / frame-local storage.

```xml
<Pad Id="..." Comment="angle per frame" Bounds="611,410,35,43" ShowValueBox="true" isIOBox="true" Value="-0.02, 0, 0">
  <p:TypeAnnotation LastCategoryFullName="3D" LastDependency="VL.CoreLib.vl">
    <Choice Kind="TypeFlag" Name="Vector3" />
  </p:TypeAnnotation>
</Pad>
```

Comment pads add `<p:ValueBoxSettings><p:stringtype p:Assembly="VL.Core" p:Type="VL.Core.StringType">Comment</p:stringtype></p:ValueBoxSettings>`.
Generic type annotations use `<p:TypeArguments><TypeReference><Choice Kind="ImmutableTypeFlag" Name="Float32" /></TypeReference></p:TypeArguments>`.

## 5. Regions

A region = a `Node` whose first Choice is `StatefulRegion`/`RegionFlag` with `Fixed="true"`,
then a `CategoryReference`, then the region-kind Choice. Inner patches carry
`ManuallySortedPins="true"`. Data crosses borders ONLY via border control points
(splicers, accumulators) — never link directly out of a region.

| Region | Category | 3rd Choice Kind | Inner patches | Notes |
|---|---|---|---|---|
| If | Primitive | `ApplicationStatefulRegion` | Create, Then | pin: Condition; passthrough when false |
| ForEach | Primitive | `ApplicationStatefulRegion` | Create, Update, Dispose | splicers per slice; loop pins: Index, Break, Keep; count = lowest slice count |
| ForEach (Max) | Primitive | `ApplicationStatefulRegion` | same | max slice count instead |
| Repeat | Primitive | `ApplicationStatefulRegion` | Create, Update, Dispose | pin: Iteration Count; while-loop = high count + Break |
| Cache | Primitive | `ProcessStatefulRegion` | Create, Then | pins: Force, Dispose Cached Outputs, Has Changed; executes only on change |
| Try | Control | `ProcessAppFlag` | Create, Update, Dispose | pins: Success/Failure/Error Message/Exceptions, hidden Node Context + User Exceptions Channel |
| Delegate | Primitive | `ApplicationRegion` (stateless!) | one unnamed | outputs operation as value on `Functionality` pin |
| Using | Primitive | `ApplicationStatefulRegion` | Create, Update | disposal scope |
| ManageProcess | (VL.CoreLib) | `ProcessAppFlag` | Create, Update, Dispose | pins: Enabled, Lifespan, hidden Reset |
| Do | Control | `ProcessAppFlag` | Create, Update, Dispose | enforces execution order via pins |
| Comment | Control | `ProcessAppFlag` | Create, Update, Dispose | content not executed |
| SingleInstance (PerApp) | — | `ProcessAppFlag` (plain node) | — | one instance per app, fed by delegate Producer |
| This | — | `ProcessNode` (Builtin) | — | outputs enclosing process instance |
| Reference | — | `ProcessAppFlag` | — | reference access to data |

Region skeleton (If) — verified against `knowledge/templates/vl/basic_vl_objects.vl` and production patches:

`ControlPoint` splicers are **direct children of Node**, appearing **before** the wrapping Patch.
Content nodes go **directly inside the wrapping `<Patch>`** — **no `<Canvas>`, no `<Fragment>`**.
Named lifecycle patches (Create/Then or Create/Update/Dispose) precede content nodes.

```xml
<Node Bounds="180,330,200,200" Id="...">
  <p:NodeReference LastCategoryFullName="Primitive" LastDependency="Builtin">
    <Choice Kind="StatefulRegion" Name="Region (Stateful)" Fixed="true" />
    <CategoryReference Kind="Category" Name="Primitive" />
    <Choice Kind="ApplicationStatefulRegion" Name="If" />
  </p:NodeReference>
  <Pin Id="..." Name="Condition" Kind="InputPin" />
  <!-- ControlPoints BEFORE the wrapping Patch (direct children of Node) -->
  <ControlPoint Id="topSplicer"    Bounds="194,336" Alignment="Top" />
  <ControlPoint Id="bottomSplicer" Bounds="194,490" Alignment="Bottom" />
  <!-- Single wrapping Patch: lifecycle named patches + content directly inside -->
  <Patch Id="innerPatchId" ManuallySortedPins="true">
    <Patch Id="createId" Name="Create" ManuallySortedPins="true" />
    <Patch Id="thenId"   Name="Then"   ManuallySortedPins="true" />
    <!-- content nodes go here as direct children — NO Canvas, NO Fragment -->
  </Patch>
</Node>
```

**Lifecycle patch names per region type** (confirmed from template):

| Region | Lifecycle patches |
|---|---|
| If | Create, Then |
| Cache | Create, Then |
| ForEach / ForEach (Max) / Repeat | Create, Update, Dispose |
| Using | Create, Update |
| ManageProcess / Comment / Try / Do | Create, Update, Dispose |
| Delegate | (single unnamed patch) |

**Frame/Overlay** (visual only): `<Overlay Id="..." Name="title" Bounds="x,y,w,h"><p:ColorIndex p:Type="Int32">7</p:ColorIndex></Overlay>` — sibling of nodes inside the canvas.

## 6. Channels

- `IChannel<T>`: named, typed, observable, bi-directional value container.
- **PublicChannel node**: app-wide channel at a path; definitions persist in a sibling `.pc` file.
- **GlobalChannel / ChannelHub nodes**: `TryAddChannel`, `GetOrAddChannel` (Got Created output).
- Use channels for app-wide parameters, UI bindings, cross-patch/document communication.
  Prefer plain links/pads for local dataflow.
- C# side: `IChannelHub.HubForApp`, `hub.TryGetChannel(path)`; NEVER `TryAddChannel`
  (creates null-valued channels). Retry-bind each frame until found. `[CanBePublished]`
  on a class publishes all properties as sub-channels.

## 7. Reactive

- Category `Reactive`: ForEach [Reactive] region (over event values in time, can hold state),
  `ForEach (Keep)` filters, OfType/Where/Skip/Delay/Scan/Switch/Merge.
- Back to mainloop: `HoldLatest` (latest value), `Sampler` (spread of values since last frame), `S+H`.
- Threading: `ReactOnMainThread` region, `ToMainThread`, `StartEagerly` (channel → observable push).
- Rule: only send **Record** values as event data (immutable, thread-safe).

## 8. Delegates

Anonymous operations passed as values. Define with the **Delegate region** (body never executed
by the region; output on `Functionality` pin). Invoke with the `Invoke` node matching the
parameter count, or feed into delegate inputs (`Where [Spread]`, Apply nodes, SingleInstance).
Type annotation: `Choice Kind="TypeFlag" Name="Delegate (0 -&gt; 1)"` + `p:TypeArguments`,
category `Primitive.Delegates`.

## 9. C# interop (the tight connection)

- VL operation ↔ static method; VL process ↔ class instance (Create/Update/Dispose per node).
- `[ProcessNode]` on a public class = stateful node: one instance per patch node, Update()
  each frame, NodeContext constructor injection, IDisposable cleanup, live reload.
- Visibility of nodes comes from `public` + `[assembly: ImportAsIs]` (or ImportNamespace/ImportType).
- `[Pin]` on parameters: Name, Visibility (Visible/Optional/Hidden/OnlyInspector), PinGroupKind.
- Source workflow: reference .csproj → vvvv compiles via Roslyn, hot-reload on save
  (Dispose → fresh instance). Binary workflow: dotnet build + restart.
- Target `net8.0`; vvvv NuGet feed: `https://teamcity.vvvv.org/guestAuth/app/nuget/v1/FeedService.svc/`.

## 10. Node call XML (referencing nodes from packages)

```xml
<Node Bounds="443,108,125,19" Id="...">
  <p:NodeReference LastCategoryFullName="Stride.Models" LastDependency="VL.Stride.Engine.vl">
    <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
    <Choice Kind="ProcessAppFlag" Name="Box" />
  </p:NodeReference>
  <Pin Id="..." Name="Node Context" Kind="InputPin" IsHidden="true" />
  <Pin Id="..." Name="Transformation" Kind="InputPin" />
  <Pin Id="..." Name="Entity" Kind="OutputPin" />
</Node>
```

- Second Choice: `OperationCallFlag` = stateless operation; `ProcessAppFlag` = stateful process.
- Variant names go INTO the Name: `Name="Rotation (Successive)"`, `Name="PBRMaterial (Metallic)"`.
- `LastDependency` (current) supersedes legacy `LastSymbolSource`; `"Builtin"` for built-ins.
- Node Bounds height is ALWAYS 19 (header only — pin rows render below automatically).
  Width derives from name + visible pin row lengths.
- vvvv serializes ALL pins of a node; hidden-by-default pins carry `IsHidden="true"`:
  - `Node Context` (infrastructure, always hidden on process nodes)
  - **state outputs** (the instance itself, e.g. `Output: Box` on Box, `State Output`) —
    hidden unless you want to operate on the instance
  - optional pins that are neither linked nor assigned a value
  - EXCEPTION: pin-group base pins (`Child` on RootScene) are hidden in the symbol data but
    their instances are VISIBLE by default — never hide pin-group pins.

### State outputs & method chaining (custom datatypes)

Every class/record operation node has a **state input** (the instance it operates on) and a
**state output** (the same instance for classes / the new instance for records). These are
hidden by default because usually you only want the node's *result*, not the instance.

Show and use them when you manage your own datatype instance:

```
Create [MyClass] ──state──> DoSomething [MyClass] ──state──> DoSomethingElse [MyClass] ──state──> ...
                                 │                                │
                             result out                       result out
```

- **Chaining** via state output → state input makes all methods operate on the SAME upstream
  instance and defines execution order (preferred — cleanest patch).
- Alternatively, connect several method state inputs directly to the pad/pin holding the
  instance — legal, but order is then undefined for mutable classes (yellow-socks warning).
- For **records** (immutable) methods do NOT operate on the upstream instance — every method
  creates a NEW COPY. The state output carries that new instance: you MUST write it back into
  the pad (or chain it into the next method) or your changes vanish next frame.
- For **classes** (mutable) methods modify the upstream instance itself; the state output is
  the SAME reference (chaining still recommended to define execution order).
- Pin groups serialize as separate pins: `Child`, `Child 2`, `Child 3`…
- Links: `<Link Id="..." Ids="outputPinId,inputPinId" />` — source first, children of the
  enclosing Patch (after ProcessDefinition).
