# vvvv gamma — IOBox & Node XML Quick-Reference

> **Scope:** compact reference for IOBox (Pad) type examples and frequently-used stateful
> process node XML. Kept here because no other doc has all Pad types together in ready-to-copy form.
>
> For everything else, use the authoritative docs:
> - Full XML spec + layout table: `read_knowledge("vl-file-format")` (tebjan, authoritative)
> - Document skeleton, definitions, regions, channels, C# interop: `read_knowledge("vl-building-blocks")`
> - Common Stride / Skia / Fuse / IO graphs (with pin-level notation): `read_knowledge("vl-common-graphs")`
> - SDSL shaders: `read_knowledge("vvvv-shaders")` and `list_templates()`
> - Multi-document projects: `read_knowledge("vl-project-architecture")`
>
> Do NOT edit automatically. Last review: 2026-08.

---

## 1. IOBox (Pad) Type Examples

All IOBoxes use `ShowValueBox="true" isIOBox="true"` (lowercase `i`). `Comment` is the label.

```xml
<!-- Float32 -->
<Pad Id="..." Bounds="200,230,35,15" ShowValueBox="true" isIOBox="true"
     Value="1.0" Comment="Amount">
  <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
    <Choice Kind="TypeFlag" Name="Float32" />
  </p:TypeAnnotation>
</Pad>

<!-- Integer32 -->
<Pad Id="..." Bounds="200,230,35,15" ShowValueBox="true" isIOBox="true"
     Value="5" Comment="Count">
  <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
    <Choice Kind="TypeFlag" Name="Integer32" />
  </p:TypeAnnotation>
</Pad>

<!-- Boolean toggle (checkbox) -->
<Pad Id="..." Bounds="200,230,35,35" ShowValueBox="true" isIOBox="true"
     Value="False" Comment="Enable">
  <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
    <Choice Kind="ImmutableTypeFlag" Name="Boolean" />
  </p:TypeAnnotation>
</Pad>

<!-- Boolean bang (momentary) -->
<Pad Id="..." Bounds="200,230,35,35" ShowValueBox="true" isIOBox="true"
     Value="False" Comment="Trigger">
  <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
    <Choice Kind="ImmutableTypeFlag" Name="Boolean" />
  </p:TypeAnnotation>
  <p:ValueBoxSettings>
    <p:buttonmode p:Assembly="VL.UI.Forms"
                  p:Type="VL.HDE.PatchEditor.Editors.ButtonModeEnum">Bang</p:buttonmode>
  </p:ValueBoxSettings>
</Pad>

<!-- String value -->
<Pad Id="..." Bounds="200,230,120,15" ShowValueBox="true" isIOBox="true"
     Value="hello" Comment="Text">
  <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
    <Choice Kind="TypeFlag" Name="String" />
  </p:TypeAnnotation>
</Pad>

<!-- String comment label (font=14) — section title -->
<Pad Id="..." Bounds="100,100,300,25" ShowValueBox="true" isIOBox="true"
     Value="My Section Title">
  <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
    <Choice Kind="TypeFlag" Name="String" />
  </p:TypeAnnotation>
  <p:ValueBoxSettings>
    <p:fontsize p:Type="Int32">14</p:fontsize>
    <p:stringtype p:Assembly="VL.Core" p:Type="VL.Core.StringType">Comment</p:stringtype>
  </p:ValueBoxSettings>
</Pad>

<!-- String comment description (font=9) — explanatory text -->
<Pad Id="..." Bounds="100,130,300,40" ShowValueBox="true" isIOBox="true"
     Value="This example demonstrates...">
  <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
    <Choice Kind="TypeFlag" Name="String" />
  </p:TypeAnnotation>
  <p:ValueBoxSettings>
    <p:fontsize p:Type="Int32">9</p:fontsize>
    <p:stringtype p:Assembly="VL.Core" p:Type="VL.Core.StringType">Comment</p:stringtype>
  </p:ValueBoxSettings>
</Pad>

<!-- Vector3 (x,y,z) -->
<Pad Id="..." Bounds="200,230,35,43" ShowValueBox="true" isIOBox="true"
     Value="0, 0.5, 0" Comment="Position">
  <p:TypeAnnotation LastCategoryFullName="3D" LastDependency="VL.CoreLib.vl">
    <Choice Kind="TypeFlag" Name="Vector3" />
  </p:TypeAnnotation>
</Pad>

<!-- Color RGBA -->
<Pad Id="..." Bounds="200,230,136,15" ShowValueBox="true" isIOBox="true"
     Value="1, 0.5, 0, 1" Comment="Color">
  <p:TypeAnnotation LastCategoryFullName="Color" LastDependency="VL.CoreLib.vl">
    <Choice Kind="TypeFlag" Name="RGBA" />
  </p:TypeAnnotation>
</Pad>

<!-- Spread<Float32> — multi-value display -->
<Pad Id="..." Bounds="200,230,35,55" ShowValueBox="true" isIOBox="true">
  <p:TypeAnnotation LastCategoryFullName="Collections" LastDependency="VL.CoreLib.vl">
    <Choice Kind="TypeFlag" Name="Spread" />
    <p:TypeArguments>
      <TypeReference LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
        <Choice Kind="TypeFlag" Name="Float32" />
      </TypeReference>
    </p:TypeArguments>
  </p:TypeAnnotation>
  <p:Value>
    <Item>0.25</Item>
    <Item>0.5</Item>
    <Item>0.75</Item>
  </p:Value>
</Pad>

<!-- Spread<RGBA> -->
<Pad Id="..." Bounds="200,230,141,95" ShowValueBox="true" isIOBox="true">
  <p:TypeAnnotation LastCategoryFullName="Collections" LastDependency="VL.CoreLib.vl">
    <Choice Kind="TypeFlag" Name="Spread" />
    <p:TypeArguments>
      <TypeReference LastCategoryFullName="Color" LastDependency="VL.CoreLib.vl">
        <Choice Kind="TypeFlag" Name="RGBA" />
      </TypeReference>
    </p:TypeArguments>
  </p:TypeAnnotation>
  <p:Value>
    <Item>1, 0, 0, 1</Item>
    <Item>0, 1, 0, 1</Item>
    <Item>0, 0, 1, 1</Item>
  </p:Value>
</Pad>
```

---

## 2. Region Patterns

Region structure — verified against `knowledge/templates/vl/basic_vl_objects.vl` and production .vl files:
- `ControlPoint` splicers are **direct children of Node**, serialized **before** the wrapping Patch
- Content nodes (and pads) go **directly inside the wrapping `<Patch>`** — no `<Canvas>`, no `<Fragment>`
- Named lifecycle patches (Create/Update/Dispose/Then) appear before content in the wrapping Patch
- All patches in the wrapping Patch carry `ManuallySortedPins="true"`
- Links from/to ControlPoints are in the **outer** patch (parent canvas)

### If Region

`Create` + `Then` patches. Condition pad → `Condition` pin.

```xml
<!-- Condition input -->
<Pad Id="condPad" Bounds="200,270,35,35" ShowValueBox="true" isIOBox="true"
     Value="False" Comment="Condition">
  <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
    <Choice Kind="ImmutableTypeFlag" Name="Boolean" />
  </p:TypeAnnotation>
</Pad>

<!-- Input value flowing through the region -->
<Pad Id="inPad" Bounds="200,230,35,15" ShowValueBox="true" isIOBox="true" Value="0">
  <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
    <Choice Kind="TypeFlag" Name="Float32" />
  </p:TypeAnnotation>
</Pad>

<!-- If region node — give W/H enough for content -->
<Node Bounds="180,330,200,160" Id="ifNodeId">
  <p:NodeReference LastCategoryFullName="Primitive" LastDependency="Builtin">
    <Choice Kind="StatefulRegion" Name="Region (Stateful)" Fixed="true" />
    <CategoryReference Kind="Category" Name="Primitive" />
    <Choice Kind="ApplicationStatefulRegion" Name="If" />
  </p:NodeReference>
  <Pin Id="condPin" Name="Condition" Kind="InputPin" />
  <!-- ControlPoints BEFORE the wrapping Patch -->
  <ControlPoint Id="ifTopSplicer"    Bounds="194,336" Alignment="Top" />
  <ControlPoint Id="ifBottomSplicer" Bounds="194,460" Alignment="Bottom" />
  <!-- Wrapping Patch: lifecycle patches first, then content nodes directly inside -->
  <Patch Id="ifInnerPatch" ManuallySortedPins="true">
    <Patch Id="ifCreate" Name="Create" ManuallySortedPins="true" />
    <Patch Id="ifThen"   Name="Then"   ManuallySortedPins="true" />
    <!-- content node — no Canvas wrapper, just direct child -->
    <Node Bounds="200,360,65,19" Id="thenNode">
      <p:NodeReference LastCategoryFullName="Animation" LastDependency="VL.CoreLib.vl">
        <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
        <Choice Kind="ProcessAppFlag" Name="LFO" />
      </p:NodeReference>
      <Pin Id="thenInput"  Name="Period" Kind="InputPin" />
      <Pin Id="thenOutput" Name="Phase"  Kind="OutputPin" />
    </Node>
  </Patch>
</Node>

<!-- Output pad -->
<Pad Id="outPad" Bounds="200,490,35,15" ShowValueBox="true" isIOBox="true">
  <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
    <Choice Kind="TypeFlag" Name="Float32" />
  </p:TypeAnnotation>
</Pad>

<!-- All links at the outer Patch level -->
<Link Id="..." Ids="condPad,condPin" />              <!-- condition → If.Condition -->
<Link Id="..." Ids="inPad,ifTopSplicer" />            <!-- input → Top splicer -->
<Link Id="..." Ids="ifTopSplicer,thenInput" />        <!-- Top splicer → node inside -->
<Link Id="..." Ids="thenOutput,ifBottomSplicer" />    <!-- node output → Bottom splicer -->
<Link Id="..." Ids="ifBottomSplicer,outPad" />        <!-- Bottom splicer → output -->
```

### ForEach Region

`Create` + `Update` + `Dispose` patches. Each element enters via Top splicer, result exits via Bottom.

```xml
<!-- Spread input -->
<Pad Id="spreadIn" Bounds="60,280,35,15" ShowValueBox="true" isIOBox="true">
  <p:TypeAnnotation LastCategoryFullName="Collections" LastDependency="VL.CoreLib.vl">
    <Choice Kind="TypeFlag" Name="Spread" />
    <p:TypeArguments>
      <TypeReference LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
        <Choice Kind="TypeFlag" Name="Float32" />
      </TypeReference>
    </p:TypeArguments>
  </p:TypeAnnotation>
  <p:Value><Item>1</Item><Item>2</Item><Item>3</Item></p:Value>
</Pad>

<Node Bounds="40,320,200,220" Id="forEachId">
  <p:NodeReference LastCategoryFullName="Primitive" LastDependency="Builtin">
    <Choice Kind="StatefulRegion" Name="Region (Stateful)" Fixed="true" />
    <CategoryReference Kind="Category" Name="Primitive" />
    <Choice Kind="ApplicationStatefulRegion" Name="ForEach" />
  </p:NodeReference>
  <Pin Id="breakPin" Name="Break" Kind="OutputPin" />
  <!-- ControlPoints BEFORE the wrapping Patch -->
  <ControlPoint Id="feTop"    Bounds="54,326" Alignment="Top" />
  <ControlPoint Id="feBottom" Bounds="54,510" Alignment="Bottom" />
  <!-- Wrapping Patch: lifecycle patches first, then content directly inside -->
  <Patch Id="forEachInner" ManuallySortedPins="true">
    <Patch Id="feCreate"  Name="Create"  ManuallySortedPins="true" />
    <Patch Id="feUpdate"  Name="Update"  ManuallySortedPins="true" />
    <Patch Id="feDispose" Name="Dispose" ManuallySortedPins="true" />
    <!-- content node — direct child of wrapping Patch, NOT inside Canvas -->
    <Node Bounds="54,380,22,19" Id="mulNode">
      <p:NodeReference LastCategoryFullName="Adaptive" LastDependency="VL.CoreLib.vl">
        <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
        <Choice Kind="OperationCallFlag" Name="* (Scale)" />
      </p:NodeReference>
      <Pin Id="mulIn"     Name="Input"  Kind="InputPin" />
      <Pin Id="mulScalar" Name="Scalar" Kind="InputPin" DefaultValue="2" />
      <Pin Id="mulOut"    Name="Output" Kind="OutputPin" />
    </Node>
  </Patch>
</Node>

<!-- Output spread -->
<Pad Id="spreadOut" Bounds="42,540,35,15" ShowValueBox="true" isIOBox="true">
  <p:TypeAnnotation LastCategoryFullName="Collections" LastDependency="VL.CoreLib.vl">
    <Choice Kind="TypeFlag" Name="Spread" />
    <p:TypeArguments>
      <TypeReference LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
        <Choice Kind="TypeFlag" Name="Float32" />
      </TypeReference>
    </p:TypeArguments>
  </p:TypeAnnotation>
</Pad>

<Link Id="..." Ids="spreadIn,feTop" />       <!-- spread → Top splicer -->
<Link Id="..." Ids="feTop,mulIn" />           <!-- Top splicer → node input -->
<Link Id="..." Ids="mulOut,feBottom" />       <!-- node output → Bottom splicer -->
<Link Id="..." Ids="feBottom,spreadOut" />    <!-- Bottom splicer → output spread -->
```

---

## 3. Common Stateful Process Node XML

These are the most frequently wired process nodes. Pin names verified from vl-common-graphs.md.

### LFO — oscillator (0→1 over Period seconds)

```xml
<Node Bounds="200,300,45,19" Id="lfoId">
  <p:NodeReference LastCategoryFullName="Animation" LastDependency="VL.CoreLib.vl">
    <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
    <Choice Kind="ProcessAppFlag" Name="LFO" />
  </p:NodeReference>
  <Pin Id="lfoPeriod"   Name="Period"       Kind="InputPin" DefaultValue="2" />
  <Pin Id="lfoPause"    Name="Pause"        Kind="InputPin" />
  <Pin Id="lfoReset"    Name="Reset"        Kind="ApplyPin" />
  <Pin Id="lfoPhase"    Name="Phase"        Kind="OutputPin" />
  <Pin Id="lfoNewCycle" Name="On New Cycle" Kind="OutputPin" />
</Node>
```

### S+H (Sample and Hold) — hold last value when condition is false

```xml
<Node Bounds="164,348,40,19" Id="shId">
  <p:NodeReference LastCategoryFullName="Control" LastDependency="VL.CoreLib.vl">
    <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
    <Choice Kind="ProcessAppFlag" Name="S+H" />
    <CategoryReference Kind="Category" Name="Control" NeedsToBeDirectParent="true" />
  </p:NodeReference>
  <Pin Id="shVal"    Name="Value"  Kind="InputPin" />
  <Pin Id="shSample" Name="Sample" Kind="InputPin" />
  <Pin Id="shOut"    Name="Value"  Kind="OutputPin" />
</Node>
```

### Changed — outputs true on the frame a value changes

```xml
<Node Bounds="200,300,65,19" Id="changedId">
  <p:NodeReference LastCategoryFullName="Control" LastDependency="VL.CoreLib.vl">
    <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
    <Choice Kind="ProcessAppFlag" Name="Changed" />
  </p:NodeReference>
  <Pin Id="chanIn"  Name="Input"  Kind="InputPin" />
  <Pin Id="chanOut" Name="Output" Kind="OutputPin" />
</Node>
```

### Switch — select one of N inputs by integer index (0-based)

```xml
<Node Bounds="190,400,85,19" Id="switchId">
  <p:NodeReference LastCategoryFullName="Control" LastDependency="VL.CoreLib.vl">
    <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
    <Choice Kind="OperationCallFlag" Name="Switch" />
    <CategoryReference Kind="Category" Name="Control" NeedsToBeDirectParent="true" />
  </p:NodeReference>
  <Pin Id="idxPin"  Name="Index"   Kind="InputPin" />
  <Pin Id="in1Pin"  Name="Input"   Kind="InputPin" />
  <Pin Id="in2Pin"  Name="Input 2" Kind="InputPin" />
  <Pin Id="in3Pin"  Name="Input 3" Kind="InputPin" />
  <Pin Id="outPin"  Name="Output"  Kind="OutputPin" />
</Node>
<!-- Index=0 → Input, Index=1 → Input 2, etc. (0-based index, 1-based pin names) -->
<!-- Add more Input pins: Ctrl+Plus on the node in the editor -->
```

### OnOpen — bang on first frame (used for initialization)

```xml
<Node Bounds="200,200,65,19" Id="onOpenId">
  <p:NodeReference LastCategoryFullName="Control" LastDependency="VL.CoreLib.vl">
    <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
    <Choice Kind="ProcessAppFlag" Name="OnOpen" />
  </p:NodeReference>
  <Pin Id="onOpenOut" Name="Output" Kind="OutputPin" />
</Node>
```

### Damper — smoothly follow a target value (spring physics)

```xml
<Node Bounds="200,350,65,19" Id="damperId">
  <p:NodeReference LastCategoryFullName="Animation" LastDependency="VL.CoreLib.vl">
    <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
    <Choice Kind="ProcessAppFlag" Name="Damper" />
  </p:NodeReference>
  <Pin Id="damperGoTo"   Name="Goto Position" Kind="InputPin" />
  <Pin Id="damperFactor" Name="Factor"        Kind="InputPin" DefaultValue="0.1" />
  <Pin Id="damperOut"    Name="Output"        Kind="OutputPin" />
</Node>
```

---

## 4. Layout Conventions

All coordinates are pixels from top-left; data flows **top → bottom**.
See `vl-file-format.md` for the full tebjan reference table (based on 769+ production files).

| Element | Typical Y |
|---|---|
| Title comment (font=14) | ~100 |
| Description comment (font=9) | ~140–190 |
| Input Pads | ~200–270 |
| Processing nodes | ~300–400 |
| Output Pads | ~420–500 |
| Renderer (Skia/Stride) | ~800+ |

| Spacing | Value |
|---|---|
| Input Pad → Node below | 50–80 px |
| Node → Node (chain) | 40–50 px |
| Node → Output Pad | 60–70 px |
| Multiple sections side-by-side | 350–400 px gap |

| Size | Typical |
|---|---|
| Standard node | `65,19` or `85,19` |
| Simple operator (+, *, …) | `22,19` or `25,19` |
| Float32 Pad | `35,15` |
| Boolean Pad (toggle/bang) | `35,35` |
| String comment | `300,25` (title) or `300,40` (description) |

**Multi-input staircase**: when N pads feed one node, offset each pad (+27x, +29y) so links don't cross.
**Bounds format**: definition/container nodes (Application, Process, Region) use `"X,Y"` (2-value);
processing nodes use `"X,Y,W,19"` (4-value, height always 19). Width: operators=25, standard=name*6+15/27.
**Layout direction**: top-to-bottom (Y increases with dataflow depth). Sources at top, sinks at bottom.
