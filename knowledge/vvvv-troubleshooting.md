---
name: vvvv-troubleshooting
description: Diagnoses and fixes common vvvv gamma errors — C# node issues (out params order, missing ImportAsIs, allocations in Update), SDSL shader mistakes (missing semicolons, override), runtime issues (memory leaks, thread safety, circular dependencies, missing change detection).
license: CC-BY-SA-4.0
compatibility: Designed for coding AI agents assisting with vvvv gamma development
metadata:
  author: Tebjan Halm
  version: "1.1"
---

# vvvv gamma Troubleshooting

## C# / ProcessNode Issues

### Node Not Appearing in Node Browser

Check in order:
1. `[assembly: ImportAsIs(Namespace="...", Category="...")]` exists
2. `[ProcessNode]` attribute on the class
3. Project targets `net8.0`
4. DLL is in the correct `lib/net8.0/` path relative to `.vl` document
5. Class is `public`, not `internal`

### "Node" Suffix in Class Name

**Fix**: Remove "Node" suffix — vvvv convention forbids it in node names.
```csharp
// WRONG: [ProcessNode] public class SteeringBehaviorNode { }
// CORRECT: [ProcessNode] public class SteeringBehavior { }
```

### Out Parameters After Inputs (Wrong Pin Order)

`out` parameters MUST come FIRST:
```csharp
// WRONG
public void Update(float input = 0f, out float result) { }
// CORRECT
public void Update(out float result, float input = 0f) { }
```

### Downstream Nodes See null/default

Always output cached result even when no computation happens:
```csharp
// WRONG — output unassigned when input unchanged
public void Update(out float result, float input = 0f)
{
    if (input != _last) { result = Compute(input); _last = input; }
    // BUG: result unassigned here!
}

// CORRECT
public void Update(out float result, float input = 0f)
{
    if (input != _last) { _cached = Compute(input); _last = input; }
    result = _cached; // Always assign
}
```

### Frame Drops / GC Spikes (Allocations in Update)

Common culprits in Update():
- `new` keyword
- LINQ (`.Where()`, `.Select()`, `.ToList()`)
- String concatenation (`+`)
- Boxing value types

**Fix**: Cache everything, pre-allocate, eliminate LINQ from hot paths.

### Missing Change Detection (High CPU)

```csharp
if (param != _lastParam)
{
    _cached = Compute(param);
    _lastParam = param;
}
result = _cached; // Always output cached
```

## SDSL Shader Issues

### Missing Semicolon After Shader Class

```hlsl
// WRONG
shader MyEffect_TextureFX : FilterBase { float4 Filter(float4 c) { return c; } }

// CORRECT — note semicolon after closing brace
shader MyEffect_TextureFX : FilterBase { float4 Filter(float4 c) { return c; } };
```

### Missing `override` Keyword

```hlsl
// WRONG — silently creates new method, parent method still runs
float4 Filter(float4 c) { return c * 0.5; }

// CORRECT
override float4 Filter(float4 c) { return c * 0.5; }
```

### Re-inheriting Already-Included Shaders

`VS_PS_Base` already includes `Transformation`, `NormalStream`, `PositionStream4`:
```hlsl
// WRONG — duplicate inheritance
shader MyDrawFX : VS_PS_Base, Transformation, NormalStream { }

// CORRECT
shader MyDrawFX : VS_PS_Base { }
```

### `static const` Inside Shader Class

`static const` must be in HLSL scope, NOT inside the `shader` block:
```hlsl
// WRONG
shader MyShader : FilterBase
{
    static const float PI = 3.14159;  // ERROR
}

// CORRECT — outside the shader block
static const float PI = 3.14159;
shader MyShader : FilterBase { /* use PI here */ };
```

## Runtime Issues

### Memory Leaks

- Missing `IDisposable` on nodes with native resources
- COM objects not disposed
- Event subscriptions not unsubscribed in `Dispose()`

### Thread Safety

`Update()` is on the main thread. Marshal from background:
```csharp
private SynchronizationContext _vlSyncContext;
public MyNode() { _vlSyncContext = SynchronizationContext.Current!; }
// From background: _vlSyncContext.Post(_ => { /* VL thread */ }, null);
```

### Circular Dependencies

**Fix**: Insert a `FrameDelay` node to break the cycle.

### Build Issues

- Target framework must be `net8.0`
- Package versions must align with vvvv's bundled versions
- For live reload: vvvv compiles .cs files; for binary DLLs: must `dotnet build` + restart vvvv
