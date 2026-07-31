namespace VvvvMcp.Core.Models;

public record PatchDependency(
    string Id,
    string Location,
    string? Version
);

public record PatchPin(
    string Id,
    string Name,
    string Kind,
    bool IsHidden,
    string? DefaultValue
);

public record PatchNodeReference(
    string? LastCategoryFullName,
    string? LastDependency,
    string? Kind,
    string? NodeName
);

public record PatchNode(
    string Id,
    string? Name,
    string? Bounds,
    PatchNodeReference Reference,
    List<PatchPin> Pins
);

public record PatchPad(
    string Id,
    string? Comment,
    string? Bounds,
    string? Value,
    bool IsIOBox,
    string? TypeName,
    string? TypeCategory
);

public record PatchLink(
    string Id,
    string SourceId,
    string TargetId
);

public record PatchCanvas(
    string Id,
    string? DefaultCategory,
    string? CanvasType,
    List<PatchNode> Nodes,
    List<PatchPad> Pads
);

public record PatchGraph(
    string DocumentId,
    string? LanguageVersion,
    string? DocumentVersion,
    List<PatchDependency> Dependencies,
    List<PatchCanvas> Canvases,
    List<PatchLink> Links,
    List<PatchNode> AllNodes,
    List<PatchPad> AllPads
);
