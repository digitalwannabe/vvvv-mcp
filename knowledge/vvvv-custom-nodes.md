---
name: vvvv-custom-nodes
description: "Helps write C# node classes for vvvv gamma — the [ProcessNode] lifecycle pattern, Update() method, out parameters, pin configuration, change detection, stateless operation nodes, the public-API import model, and service consumption via NodeContext."
license: CC-BY-SA-4.0
compatibility: Designed for coding AI agents assisting with vvvv gamma development
metadata:
  author: Tebjan Halm
  version: "1.2"
---

# Writing Custom Nodes for vvvv gamma

## What `[ProcessNode]` actually does

**`[ProcessNode]` does NOT control whether a class becomes a node.** A plain `public class Foo { public int Bar(int x) => x; }` appears in the node browser exactly the same way as one decorated with `[ProcessNode]`.

What `[ProcessNode]` DOES:
- Tells vvvv "this is a stateful node — keep ONE instance alive per node in the patch and call its `Update()` method each frame"
- Lets you set `Name`, `Category`, and `HasStateOutput`
- Engages live reload, `IDisposable` cleanup, and `NodeContext` constructor injection

What makes a class VISIBLE is: `public` access modifier + `[assembly: ImportAsIs/ImportNamespace/ImportType]`.

## ProcessNode Pattern — The Core Pattern

```csharp
[ProcessNode]
public class MyTransform : IDisposable
{
    private float _lastInput;
    private float _cachedResult;

    /// <summary>Transforms input values with caching.</summary>
    public void Update(
        out float result,       // OUT parameters FIRST
        out string error,       // More out params
        float input = 0f,       // Value inputs with defaults AFTER
        bool reset = false)
    {
        error = null;
        if (input != _lastInput || reset)
        {
            _cachedResult = ExpensiveComputation(input);
            _lastInput = input;
        }
        result = _cachedResult; // ALWAYS output cached data
    }

    public void Dispose() { /* cleanup */ }
}
```

### Non-Negotiable Rules

1. **`[ProcessNode]` attribute** on every stateful node class
2. **No "Node" in the vvvv-visible name**
3. **`out` parameters FIRST**, value inputs with defaults AFTER
4. **XML comments** on class and Update method (shown as tooltip)
5. **ZERO allocations in Update** — no `new`, no LINQ, cache everything
6. **Change detection** — only recompute when inputs actually change
7. **Always output latest data** — even when no work is done, output cached result
8. **`IDisposable`** for any node holding native/unmanaged resources

## Operation Nodes (Stateless)

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

## Change Detection Patterns

### Simple — Direct Field Comparison
```csharp
private float _lastParam;
private Result _cached;

public void Update(out Result result, float param = 0f)
{
    if (param != _lastParam)
    {
        _cached = Compute(param);
        _lastParam = param;
    }
    result = _cached;
}
```

### Reference Types — Identity Check
```csharp
if (!ReferenceEquals(newBuffer, _lastBuffer))
{
    ProcessBuffer(newBuffer);
    _lastBuffer = newBuffer;
}
```

### Multi-Input — Hash Check
```csharp
private int _lastHash;
private Config _cached;

public void Update(out Config config, float a = 0f, int b = 0, string c = "")
{
    int hash = HashCode.Combine(a, b, c);
    if (hash != _lastHash)
    {
        _cached = new Config(a, b, c);
        _lastHash = hash;
    }
    config = _cached;
}
```

## Constructor Patterns

```csharp
// Simple node (no special context)
public MyNode() { }

// Node needing NodeContext
public MyNode(NodeContext nodeContext)
{
    _nodeContext = nodeContext;
    // nodeContext.AppHost.IsExported, nodeContext.AppHost.Services, etc.
}
```

## Pin Visibility

```csharp
public void Update(
    out Spread<float> result,
    [Pin(Visibility = PinVisibility.OnlyInspector)] out string error,
    float input = 0f,
    [Pin(Visibility = PinVisibility.Optional)] bool advanced = false)
```

## Assembly Import Attributes

For your nodes to be visible in vvvv's node browser, add to `Initialization.cs`:
```csharp
// Single namespace, one category
[assembly: ImportAsIs(Namespace = "MyCompany.MyLib", Category = "MyLib")]

// Multiple namespaces/categories
[assembly: ImportNamespace("MyCompany.MyLib.Renderers", Category = "MyLib.Rendering")]
[assembly: ImportNamespace("MyCompany.MyLib.Utils",     Category = "MyLib.Utils")]

// Specific types only
[assembly: ImportType(typeof(MyNode), Category = "MyLib")]
```

## .csproj Setup

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="VL.Core" Version="2025.7.*" />
    <PackageReference Include="VL.Core.Import" Version="2025.7.*" />
  </ItemGroup>
</Project>
```
