# vvvv gamma — Package Reference

This document describes the key NuGet packages available for vvvv gamma.

---

## Core Packages (always available)

### VL.CoreLib
The default library included with every vvvv installation. Provides fundamental nodes for all basic patching needs.

**Categories:**
- `2D` — Vector2, Rectangle, Circle, 2D transforms, collisions
- `3D` — Vector3, Box, Sphere, 3D transforms, collisions
- `3D.Transform` — TransformSRT (Scale-Rotation-Translation), Translate, Scale, Rotate
- `3D.Vector3` — Vector (Join), Split, Normalize, Length, Dot, Cross
- `Adaptive` — +, -, *, / that work on int, float, Vector2/3/4, string, etc.
- `Animation` — LFO, Stopwatch, Damper, Oscillator (time-based filters/generators)
- `Collections` — Spread, Sequence, Dictionary, HashSet, GetSlice, SetIndex, etc.
- `Color` — RGBA color type, HSL/HSV conversions, color lerp
- `Control` — FlipFlop, MonoFlop, Toggle, Bang, Changed, FrameDelay
- `IO` — Mouse, Keyboard, Touch input; File I/O; Path utilities; basic networking
- `Math` — Abs, Clamp, Lerp, Map, Mod, Sin, Cos, Pow, Sqrt, Min, Max
- `Primitive` — Bool, Byte, Int32, Int64, Float32, Float64, Char, String types
- `Reactive` — Observable nodes, IChannel, Rx.NET operators
- `System` — XML, JSON, DateTime, Serialization, Environment

---

## 3D Rendering

### VL.Stride
The primary 3D rendering package, built on the Stride game engine.

**Key categories:**
- `Stride` — SceneWindow (3D viewport), RootScene (entity tree root), ForwardRenderer
- `Stride.Models` — Box, Sphere, Cylinder, Plane, Torus, Cone, Capsule (3D meshes)
- `Stride.Materials` — material creation, PBR materials, texture assignment
- `Stride.Cameras` — cameras (perspective, orthographic)
- `Stride.Lights` — ambient light, directional light, point light, spot light
- `Stride.Effects` — shaders and effects applied to entities
- `Stride.Textures` — texture loading and manipulation
- `Stride.Transform` — Entity transformations

**Common patch pattern:**
```
[TransformSRT] → [Box] → [RootScene] → [SceneWindow]
```

### VL.Fuse
GPU visual programming using SDSL shader composition. Builds shader graphs visually.

**Key uses:** Custom shaders, particles, procedural geometry, GPU computation

---

## 2D Rendering

### VL.Skia
2D vector graphics using the Skia engine.
- Canvas-based 2D rendering
- Text rendering, paths, shapes
- Image loading and manipulation

### VL.ImGui
Immediate-mode GUI library (Dear ImGui).
- Buttons, sliders, text inputs, color pickers
- Debug panels, parameter editors

---

## Audio

### VL.Audio
Audio playback and processing.
- `Audio` category: Buffer, BufferPlayer, AudioSignal
- DSP effects, analysis, recording
- NAudio backend

---

## Computer Vision & Machine Learning

### VL.OpenCV
OpenCV integration for image processing and computer vision.
- Camera input, image capture
- Image filtering and transformations
- Feature detection, optical flow

### VL.ML.ONNX
ONNX Runtime for running machine learning models.
- Object detection (YOLO)
- Image classification
- Pose estimation

---

## Hardware & Protocols

### VL.Devices.Kinect2
Azure Kinect depth camera integration.
- Color + depth image streams
- Body tracking

### VL.Devices.ZED
ZED stereo depth camera.
- High-quality depth maps
- Spatial mapping

### VL.Devices.Ultraleap
Ultraleap (Leap Motion) hand tracking.
- Hand and finger positions
- Gesture recognition

### VL.IO.OSC
Open Sound Control (OSC) protocol.
- `OSC` category: Send, Receive, Message
- Common for communication with Max/MSP, Pure Data, TouchDesigner, lighting desks

### VL.IO.MIDI
MIDI protocol.
- MIDI input/output devices
- Note, CC, pitch bend messages

### VL.IO.MQTT
MQTT messaging protocol.
- IoT device communication
- Pub/sub topics

### VL.IO.ArtNet
DMX lighting control via Art-Net protocol.
- Stage lighting control
- DMX universes

---

## Networking & Web

### VL.IO.WebSocket
WebSocket client and server.
- Real-time bidirectional communication
- JSON messaging patterns

### VL.IO.HTTP
HTTP client/server nodes.
- REST API calls
- Web server capabilities

### VL.IO.Redis
Redis in-memory data store.
- Fast key-value storage
- Pub/sub messaging

---

## Animation & Simulation

### VL.Animation
Advanced animation tools.
- Tweening (Lerp, Ease curves)
- Keyframe animation
- Timeline sequencing

### VL.Physics
Physics simulation (often via BepuPhysics or Bullet).
- Rigid bodies
- Collision detection

---

## UI Frameworks

### VL.Elementa
Declarative GUI system for building interactive UIs within vvvv patches.

### VL.Avalonia
Avalonia UI framework integration.
- XAML-based UI
- Cross-platform desktop apps

---

## How to Add a NuGet Package

In a `.vl` document, add a `NugetDependency` as a direct child of `Document`:
```xml
<NugetDependency Id="..." Location="VL.PackageName" Version="2025.7.*" />
```

Or use vvvv's built-in NuGet manager: Edit > Manage NuGets.

---

## Package Discovery

- **Official catalog:** https://vvvv.org/packs
- **NuGet.org** (search for "VL." prefix)
- **GitHub topics:** https://github.com/topics/vl
- **vvvv forum WIP section:** https://forum.vvvv.org/c/wip/27
