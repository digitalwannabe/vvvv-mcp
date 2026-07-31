---
name: vvvv-channels
description: "vvvv gamma Channel system from C# — IChannelHub, public channels, [CanBePublished] attributes, hierarchical data propagation, channel subscriptions, bang channels, spread sub-channels. Use when reading/writing public channels, working with IChannelHub, reactive/observable data flow, two-way data binding."
license: CC-BY-SA-4.0
compatibility: Designed for coding AI agents assisting with vvvv gamma development
metadata:
  author: Tebjan Halm
  version: "1.1"
---

# vvvv gamma Channels — C# Integration

## What Are Channels

Channels are **named, typed, observable value containers** — the central reactive data flow mechanism in vvvv gamma.

- Each channel has a **path** (string), a **type**, and a **current value**
- Setting a value fires all subscribers (reactive push)
- Channels persist state across sessions
- vvvv provides built-in channel bindings for MIDI, OSC, Redis, and UI

## Public Channels and IChannelHub

```csharp
using VL.Core.Reactive;

// Get the app-wide channel hub (singleton)
var hub = IChannelHub.HubForApp;

// Safe lookup — returns null if channel doesn't exist yet
IChannel<object>? ch = hub.TryGetChannel("MyApp.Settings.Volume");

// Read the current value
object? value = ch.Object;

// Write a new value (fires all subscribers)
ch.Object = newValue;
```

**CRITICAL: NEVER use `hub.TryAddChannel()`** — only use `TryGetChannel`. `TryAddChannel` creates channels with null values, causing `NullReferenceException` in vvvv's SubChannelsBinding.

## [CanBePublished] Attribute

For .NET types to be exposed as channels:

```csharp
using VL.Core.EditorAttributes;

[CanBePublished(true)]
public class MyModel
{
    public float Volume { get; set; } = 0.5f;
    public bool Muted { get; set; } = false;
    public string Label { get; set; } = "Default";

    [CanBePublished(false)]  // Hidden from channel system
    public string InternalId { get; } = Guid.NewGuid().ToString();
}
```

## Channel Path Conventions

```
Root.Page.Zone.Group.Parameter          — leaf parameter
Root.Page.Zone                          — hierarchy node (model object)
Root.Page.Items[0].PropertyName         — spread element sub-channel
Root.Page.Items[2].DeleteInstance       — indexed bang channel
```

## Retry-Bind Pattern

Channels may not exist when your node starts. Retry each frame:

```csharp
[ProcessNode]
public class MyChannelReader : IDisposable
{
    private IChannel<object>? _channel;

    public void Update(out float value)
    {
        if (_channel == null)
        {
            var hub = IChannelHub.HubForApp;
            if (hub != null)
                _channel = hub.TryGetChannel("Settings.Audio.Volume");
        }
        value = _channel?.Object is float f ? f : 0f;
    }

    public void Dispose() { _channel = null; }
}
```

## Reactive Subscriptions

```csharp
IChannel<object>? ch = hub.TryGetChannel("Settings.Audio.Volume");
if (ch != null)
{
    IDisposable subscription = ch.Subscribe(new CallbackObserver(value =>
    {
        if (value is float f)
            ApplyVolume(f);
    }));
    // Always dispose subscription in node's Dispose()
}
```

## Bang Channels

For trigger/event properties, use `System.Reactive.Unit`:

```csharp
using System.Reactive;

[CanBePublished(true)]
public class MyInstance
{
    public Unit DeleteInstance { get; set; }     // Bang channel
    public Unit InsertAfterInstance { get; set; } // Bang channel
}
```

## Hierarchical Propagation

- Write a root record → all child channels auto-update
- Write a leaf channel → parent channels auto-update
- This is built into vvvv's SubChannel system

```csharp
// Load: write to root channel → ALL children update automatically
rootChannel.Object = loadedModel;
```

## Critical Rules

1. **NEVER `TryAddChannel`** — only `TryGetChannel`
2. **Always retry-bind** — channels appear after model initialization
3. **`[CanBePublished(true)]` required** on .NET types for channel publication
4. **Always dispose subscriptions** in `Dispose()`
5. **`System.Reactive.Unit` for bangs** — not `float` or `bool`
6. **Suppression flags** to prevent feedback loops when writing to subscribed channels
