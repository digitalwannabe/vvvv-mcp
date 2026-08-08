# vvvv gamma — Common Graphs (mined from 2053 help patches)

> **Purpose:** the recurring subgraphs of real vvvv patches, with exact node names,
> categories and pin-level links. Use these as construction templates for build_patch.
> Mined from all `help/**.vl` of VL.CoreLib, VL.Stride, VL.Skia, VL.ImGui, VL.Audio,
> VL.Fuse, VL.IO.* and ~150 community packs (6277 distinct nodes, 241k co-occurrences).
> For format rules see vl-building-blocks.md; for XML skeletons see vl-patterns.md.

Node notation: `Name [Category]`. Link notation: `A.Output → B.Input`.

---

## Stride 3D

### P1. Basic 3D scene (52 help patches contain all 4 core nodes)
```
RootScene [Stride]        .Output → SceneWindow [Stride].Input
OrbitCamera [Stride.Cameras].Output → SceneWindow.Camera
DirectionalLight [Stride.Lights].Entity → RootScene.Child
SkyboxLight [Stride.Lights].Entity   → RootScene.Child   (pin group auto-indexes)
Box/Sphere/Plane [Stride.Models].Entity → RootScene.Child
LFO [Animation].Phase → OrbitCamera.Initial Yaw          (optional animation)
```
Minimal example: VL.Addons.Stride help "Reference WireBox.vl".

Minimal rotating box (5 nodes, 5 links):
```
Rotation (Successive) [3D.Transform]  .Result → Box [Stride.Models].Transformation
  values: {"Angular Delta":"0.01, 0, 0"}     ← X-axis rotation, ~0.6 rpm
Box.Entity → RootScene [Stride].Child
DirectionalLight [Stride.Lights].Entity → RootScene.Child
RootScene.Output → SceneWindow [Stride].Input
```
No LFO, no multiply, no material node needed — Rotation (Successive) accumulates
internally, Box has a default PBR material, SceneWindow has Enable Default Camera.

### P2. PBR material with texture maps
```
ValueMap [Stride.Materials.Inputs].Output → PBRMaterial (Metallic).Metalness / .Roughness
ColorMap [Stride.Materials.Inputs].Output → PBRMaterial (Metallic).Diffuse
PBRMaterial (Metallic).Output → Sphere.Material          (pin: Material)
Sphere.Entity → RootScene.Child
FileTexture [Stride.Assets].Texture → SkyboxLight.Cube Map
```

### P3. Dynamic mesh (procedural geometry)
```
ForEach region builds vertex spread → Cache region (gate on change)
Cache.Has Changed → DynamicMesh [Stride.Models.Meshes].Update Buffer
Pos3Norm3Tex2 [Stride.API.Graphics.VertexDeclaration].Output → DynamicMesh.Vertex Declaration
DynamicMesh.Output → MeshModel.Mesh
PBRMaterial.Output → MeshModel.Material
MeshModel.Output → ModelEntity.Model
ModelEntity.Entity → RootScene.Child
```

### P4. Custom mesh renderer
```
MeshRenderer.Output → RenderEntity.Input
RenderEntity.Output → RootScene.Child
```

## Skia 2D

### P5. Paint → layer → group → renderer (THE Skia idiom, 242 patches)
```
Stroke/Fill [Graphics.Skia.Paint].Output → Circle/Rectangle/...Paint
Circle [Graphics.Skia.Layers].Output → Group [Graphics.Skia].Input
Group.Output → Renderer [Graphics.Skia].Input
```

### P6. Spectral group + mouse interaction (spread of layers)
```
Mouse [Graphics.Skia.IO].Context → Group.Input            (world input)
Mouse.Position In World → Distance [Math].Input
Distance.Result → Map [Math.Ranges].Input → Map.Output → Circle.Radius
Circle inside ForEach (spectral) → Group (Spectral) [Graphics.Skia]
Group (Spectral).Output → Group.Input 2 → Renderer.Input
```

### P7. ImGui UI
```
REGION:ImGui.Output → Renderer [Graphics.Skia].Input       (97 patches)
SetNextItemWidth [ImGui.Commands.Immediate].Context → Slider (Float) [ImGui.Widgets].Context
widget pins: Label (string) + Channel (IChannel<T>)
state: Channel [Reactive] → widget.Channel; write back via SetValue
```

## Animation & control

### P8. Animation drive
```
LFO [Animation].Output → * [Math] → Damper [Animation].Goto Position
Stopwatch [Animation].Time → * .Input
LFO.On New Cycle → Toggle.Flip  /  Cache.Force
LFO.Cycles → MOD [Math].Input → Ord2Enum [Primitive.Enum]   (enum cycler)
```

### P9. Startup bang
```
OnOpen [Control].Output → X.Read / Refresh / Cache.Force / If.Condition
```

### P10. Change detection fan-in
```
Changed.Result → OR [Logic].Input … → Refresh/Force
IsAssigned.Result → If.Condition                              (null guard)
CastAs.Success → If.Condition
```

### P11. Switch by index
```
KeyToggle/MOD/LFO.Cycles → Switch [Control].Index
Switch.Output → …
```

## Reactive & channels

### P12. Channel write/read (26 patches)
```
IOBox → Channel [Reactive].Value          (write)
Channel.Output → Value [Reactive.Channel].Input → Value.Value → result
Channel.Output → SetValue.Input  (+ bang → SetValue.Apply, value → SetValue.Value)
EnsureValue [Reactive.Channel] for defaults
```

### P13. Reactive ForEach → mainloop
```
observable → ForEach [Reactive].Messages
ForEach.Result → HoldLatest [Reactive].Async Notifications
HoldLatest.On Data → SetValue.Apply
```

## TextureFX (Stride textures)

### P14. TextureFX chain preview (186 patches)
```
FileTexture [Stride.Assets].Output → SomeFilter [Stride.Textures.Filter.*].Input
filter.Output → TextureWindow [Stride.Textures].Input
animated params: Integrator [Animation.FrameBased].Value → BubbleNoise.Time
                 BubbleNoise.Output → ValueMap [Stride.TextureFX.Inputs].Value → filter param
```

## Fuse (GPU shader graphs)

### P15. GPU particle pipeline
```
SetCommonAttributes [Fuse.Particles.Util].Output → ProbabilityEmitter.On Emit
ProbabilityEmitter.Output → EmissionStage [Fuse.Particles].Emitter
EmissionStage.Output → Group (ComputeStage) [Fuse.Compute].Compute Stage
IntegrationStage [Fuse.Particles.Integration].Output → .Compute Stage 2
BrownianMotion [Fuse.Calculus.Vector.3D].Vector → IntegrationStage.Force
StructuredBufferResource [Fuse.Compute].Output → Group (ComputeStage).Resource
Group (ComputeStage).Output → ComputeSystem [Fuse.Common.Compute].Compute Stage
```
Draw:
```
Position (Particle) [Fuse.Particles.Attribute].graph → Sprite (Effect) [Fuse.Draw].Position
IsAlive.Not → Sprite (Effect).Skip
Sprite (Effect).Output → BufferToRenderEntity [Fuse.Compute.Draw].Draw Shader
StructuredBufferResource.Output → BufferToRenderEntity.Resource
BufferToRenderEntity.Entity → Group [Stride].Child / HelpWindow (3D View).Scene
```
Fuse idioms: attribute reads expose a lowercase **`graph`** output; attribute writes via
`Set [Fuse.Value]` (output **`Void`**) → `Group (GpuVoid)` → `ComputeStage.ShaderNode`.

## IO / networking

### P16. OSC send/receive
```
OSCClient.Output → SendMessage.Input; trigger: Changed.Result → SendMessage.Apply
OSCServer.Data → OSCReceiver.Input
Channel.Output → BindToOSC.Channel
```
(Midi/TCP/WebSocket/MQTT mirror this: client process node → Send* with Apply bang;
receive → ToObservable/HoldLatest.)

### P17. Video in
```
VideoIn.Output → VideoSourceToTexture.Input → TextureWindow
property access guarded: GetProperty → IsAssigned.Result → If.Condition
```

### P18. Serialization roundtrip
```
Serialize.Result → Deserialize.Content
XMLReader.Output → Deserialize.Content;  Serialize.Result → XMLWriter.Data
```

## Avalonia UI

### P19. Fluent style chain
```
SetMargin → SetVerticalAlignment → SetHorizontalAlignment → Grid.Style   (chained via Style pins)
Grid → AvaloniaLayer.Content
DarkTheme.Dark Theme → AvaloniaLayer.Requested Theme Variant
AvaloniaLayer.Output → Renderer.Input
widgets bind: Channel.Output → <Widget>.<X> Channel
```

---

## Usage notes for build_patch

- These graphs map 1:1 to build_patch specs: nodes[] with name+category, links[] with
  `key.PinName`. Pin groups (RootScene.Child) auto-index when linked multiple times.
- The pin names here are from real patches — trust them over guesses.
- When a pattern needs a region (ForEach/If/Cache), create the region node with kind
  shown in vl-building-blocks.md §5, then build_patch the inner content separately
  (region inner patches are separate canvases).
