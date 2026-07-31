---
name: vvvv-dotnet
description: ".NET integration in vvvv gamma — NuGet packages, .csproj setup, the [assembly: ImportAsIs] attribute, vector type interop (System.Numerics vs Stride.Core.Mathematics), async patterns, IDisposable, blittable structs. Use when configuring .csproj, adding NuGet references, nodes not appearing in browser, working with .NET types, async operations, or interop."
license: CC-BY-SA-4.0
compatibility: Designed for coding AI agents assisting with vvvv gamma development
metadata:
  author: Tebjan Halm
  version: "1.1"
---

# .NET Integration in vvvv gamma

## .csproj Configuration

Minimal `.csproj` for a vvvv gamma C# plugin:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OutputPath Condition="'$(Configuration)'=='Release'">..\..\lib\net8.0\</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="VL.Core" Version="2025.7.*" />
    <PackageReference Include="VL.Core.Import" Version="2025.7.*" />
    <!-- For 3D: -->
    <PackageReference Include="VL.Stride.Runtime" Version="2025.7.*" />
  </ItemGroup>
</Project>
```

- **Target framework**: `net8.0` required
- **Output path**: `lib/net8.0/` for NuGet packaging; not used during live reload

## Required Global Usings

```csharp
global using VL.Core;
global using VL.Core.Import;
global using VL.Lib.Collections;
```

## Required Assembly Attribute

For vvvv to discover your nodes:
```csharp
[assembly: ImportAsIs(Namespace = "MyNamespace", Category = "MyLib")]
```

Without this, nodes will NOT appear in the node browser.

## How vvvv Uses C# Code

**Source project reference** (live reload): .vl document references a .csproj. vvvv compiles via Roslyn on every .cs save — no `dotnet build` needed.

**Binary reference** (no live reload): .vl references a DLL or NuGet package. Must `dotnet build` and restart vvvv after changes.

Shaders (.sdsl) always live-reload regardless.

## NuGet Package Sources

```xml
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="vvvv" value="https://teamcity.vvvv.org/guestAuth/app/nuget/v1/FeedService.svc/" />
  </packageSources>
</configuration>
```

## Vector Types & SIMD

- **Internal hot paths**: Use `System.Numerics.Vector3/4` (SIMD via AVX/SSE)
- **API boundaries** (Update params): Use `Stride.Core.Mathematics` types
- **Zero-cost conversion**:

```csharp
using System.Runtime.CompilerServices;

// Stride → System.Numerics (zero-cost)
ref var nv = ref Unsafe.As<Stride.Core.Mathematics.Vector3, System.Numerics.Vector3>(ref sv);

// System.Numerics → Stride (zero-cost)
ref var sv = ref Unsafe.As<System.Numerics.Vector3, Stride.Core.Mathematics.Vector3>(ref nv);
```

## IDisposable Pattern

Any node with native resources must implement `IDisposable`:
```csharp
[ProcessNode]
public class NativeWrapper : IDisposable
{
    private IntPtr _handle;

    public NativeWrapper() { _handle = NativeLib.Create(); }

    public void Update(out int result)
    {
        result = NativeLib.Process(_handle);
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            NativeLib.Destroy(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
```

## Async Patterns

Since `Update()` runs on the main thread at 60 FPS:

```csharp
[ProcessNode]
public class AsyncLoader
{
    private Task<string>? _loadTask;
    private string _cachedResult = "";

    public void Update(
        out string result,
        out bool isLoading,
        string url = "",
        bool trigger = false)
    {
        if (trigger && (_loadTask == null || _loadTask.IsCompleted))
            _loadTask = Task.Run(() => LoadFromUrl(url));

        isLoading = _loadTask != null && !_loadTask.IsCompleted;
        if (_loadTask?.IsCompletedSuccessfully == true)
            _cachedResult = _loadTask.Result;

        result = _cachedResult;
    }
}
```

## Blittable Structs for GPU/Network

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct AnimationBlendState
{
    public int ClipIndex1;
    public float ClipTime1;
    public int ClipIndex2;
    public float ClipTime2;
    public float BlendWeight;
}
```

Rules: no reference types, no `bool` (use `int`), explicit layout. Enables `Span<T>` and zero-copy serialization.

## Common Packages

| Package | Purpose |
|---|---|
| `VL.Core` | Core types, ProcessNode, Spread |
| `VL.Core.Import` | ImportAsIs attribute |
| `VL.Stride` | 3D rendering |
| `VL.Stride.Runtime` | Stride engine runtime |
| `VL.Skia` | 2D rendering |
| `VL.Fuse` | GPU visual programming |
| `VL.Audio` | Audio synthesis / NAudio |
| `VL.OpenCV` | Computer vision |
| `VL.IO.OSC` | OSC protocol |
| `VL.IO.MQTT` | MQTT messaging |

## Threading

- `Update()` is always called on the VL main thread
- Use `SynchronizationContext` to marshal back from background threads:

```csharp
private SynchronizationContext _vlSyncContext;
public MyNode() { _vlSyncContext = SynchronizationContext.Current!; }
// From background thread:
_vlSyncContext.Post(_ => { /* runs on VL thread */ }, null);
```
