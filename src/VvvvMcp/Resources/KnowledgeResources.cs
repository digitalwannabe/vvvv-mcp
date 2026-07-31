using System.ComponentModel;
using ModelContextProtocol.Server;
using VvvvMcp.Core.Services;

namespace VvvvMcp.Resources;

[McpServerResourceType]
public class KnowledgeResources
{
    private readonly KnowledgeService _knowledge;

    public KnowledgeResources(KnowledgeService knowledge)
    {
        _knowledge = knowledge;
    }

    // ── Synthesized skill files ─────────────────────────────────────────────

    [McpServerResource(Name = "vvvv Concepts", UriTemplate = "vvvv://knowledge/concepts")]
    [Description("Core vvvv gamma concepts: execution model, node types, type system, regions, IOBoxes, links, patches, Stride 3D scene structure, VL.CoreLib categories, key packages.")]
    public string GetConcepts() => Get("vvvv-concepts");

    [McpServerResource(Name = "vvvv VL File Format", UriTemplate = "vvvv://knowledge/file-format")]
    [Description("The .vl XML file format: document structure, element hierarchy, ID system, NodeReference/Choice patterns, Pins, Pads, Links, regions, TypeAnnotations, common mistakes.")]
    public string GetFileFormat() => Get("vl-file-format");

    [McpServerResource(Name = "vvvv Patching Patterns", UriTemplate = "vvvv://knowledge/patching")]
    [Description("Visual programming patterns: dataflow, regions (ForEach/If/Switch/Repeat), channels, event handling (Bang/Toggle/FrameDelay/Changed), patch organization, anti-patterns.")]
    public string GetPatching() => Get("vvvv-patching");

    [McpServerResource(Name = "vvvv Custom Nodes (C#)", UriTemplate = "vvvv://knowledge/custom-nodes")]
    [Description("Writing C# nodes for vvvv: [ProcessNode] lifecycle, Update() method, out parameters first, change detection, operation nodes, assembly import attributes.")]
    public string GetCustomNodes() => Get("vvvv-custom-nodes");

    [McpServerResource(Name = "vvvv Packages Reference", UriTemplate = "vvvv://knowledge/packages")]
    [Description("Key NuGet packages: VL.CoreLib, VL.Stride, VL.Fuse, VL.Skia, VL.Audio, VL.OpenCV, hardware/networking packages, and how to add them.")]
    public string GetPackages() => Get("vvvv-packages");

    [McpServerResource(Name = "vvvv Fundamentals", UriTemplate = "vvvv://knowledge/fundamentals")]
    [Description("vvvv gamma fundamentals: live compilation model, frame-based execution, source vs binary project references, node categories, pin/pad/link concepts.")]
    public string GetFundamentals() => Get("vvvv-fundamentals");

    [McpServerResource(Name = "vvvv SDSL Shaders", UriTemplate = "vvvv://knowledge/shaders")]
    [Description("SDSL shader authoring: TextureFX/DrawFX/ComputeFX/ShaderFX, shader classes, mixins, streams system, keywords, GPU best practices, texture sampling.")]
    public string GetShaders() => Get("vvvv-shaders");

    [McpServerResource(Name = "vvvv .NET Integration", UriTemplate = "vvvv://knowledge/dotnet")]
    [Description(".NET integration: .csproj setup, NuGet packages, ImportAsIs attribute, vector type interop, async patterns, IDisposable, threading.")]
    public string GetDotNet() => Get("vvvv-dotnet");

    [McpServerResource(Name = "vvvv Channels", UriTemplate = "vvvv://knowledge/channels")]
    [Description("vvvv Channel system: IChannelHub, [CanBePublished], hierarchical data propagation, subscriptions, bang channels, spread sub-channels, reactive data flow.")]
    public string GetChannels() => Get("vvvv-channels");

    [McpServerResource(Name = "vvvv Troubleshooting", UriTemplate = "vvvv://knowledge/troubleshooting")]
    [Description("Diagnosing and fixing common vvvv errors: C# node issues (pin order, missing ImportAsIs, allocations), SDSL mistakes, runtime issues (leaks, threading, circular deps).")]
    public string GetTroubleshooting() => Get("vvvv-troubleshooting");

    // ── Gray Book (official vvvv documentation) ────────────────────────────

    [McpServerResource(Name = "Gray Book — Language (VL)", UriTemplate = "vvvv://knowledge/gray-book/language")]
    [Description("Official vvvv documentation — Language VL: nodes, links, patches, operations, properties, pads, IOBoxes, regions (loops/conditions/delegates), generics, type system, frames, compilation, execution order.")]
    public string GetGrayBookLanguage() => Get("gray-book-language");

    [McpServerResource(Name = "Gray Book — Extending vvvv", UriTemplate = "vvvv://knowledge/gray-book/extending")]
    [Description("Official vvvv documentation — Extending vvvv: writing nodes (C#), aspects, design guidelines, creating libraries, node factories, editor extensions, SDSL shaders, using .NET libraries, forwarding, contributing.")]
    public string GetGrayBookExtending() => Get("gray-book-extending");

    [McpServerResource(Name = "Gray Book — Libraries", UriTemplate = "vvvv://knowledge/gray-book/libraries")]
    [Description("Official vvvv documentation — Libraries: VL.CoreLib overview, collections (Spread), reactive programming (IObservable), 3D graphics (Stride), shaders, models, textures, JSON/XML/serialization.")]
    public string GetGrayBookLibraries() => Get("gray-book-libraries");

    [McpServerResource(Name = "Gray Book — Development Environment", UriTemplate = "vvvv://knowledge/gray-book/hde")]
    [Description("Official vvvv documentation — Development Environment (HDE): GUI, node browser, patch explorer, inspector, keyboard shortcuts, debugging, managing NuGets, exporting, settings.")]
    public string GetGrayBookHde() => Get("gray-book-hde");

    [McpServerResource(Name = "Gray Book — Best Practice", UriTemplate = "vvvv://knowledge/gray-book/best-practice")]
    [Description("Official vvvv documentation — Best Practice: version control, video playback/capture/recording/synchronization, text rendering, Raspberry Pi/ARM deployment, PTP, vvvv on Mac.")]
    public string GetGrayBookBestPractice() => Get("gray-book-best-practice");

    [McpServerResource(Name = "Gray Book — Getting Started", UriTemplate = "vvvv://knowledge/gray-book/getting-started")]
    [Description("Official vvvv documentation — Getting Started: introduction for .NET programmers, creative coders, beta users; concepts, keywords, templates.")]
    public string GetGrayBookGettingStarted() => Get("gray-book-getting-started");

    [McpServerResource(Name = "Gray Book — Introduction & Explanations", UriTemplate = "vvvv://knowledge/gray-book/introduction")]
    [Description("Official vvvv documentation — Explanations and introductions: conceptual articles, background material on vvvv's approach to live programming.")]
    public string GetGrayBookIntroduction() => Get("gray-book-introduction");

    // ── Private helpers ────────────────────────────────────────────────────

    private string Get(string name)
    {
        if (!_knowledge.IsLoaded)
            return $"Knowledge base not loaded. Set VVVV_MCP_KNOWLEDGE environment variable.";
        return _knowledge.GetFile(name)?.Content
            ?? $"Knowledge document '{name}' not found.";
    }
}
