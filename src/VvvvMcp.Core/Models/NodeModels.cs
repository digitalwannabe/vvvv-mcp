namespace VvvvMcp.Core.Models;

public enum NodeType
{
    Unknown,
    Record,
    Class,
    Process,
    Operation,
    Method,
    Setter,
    Getter
}

public record NodePin(
    string Name,
    string Type,
    string Summary = "",
    string DefaultValue = "",
    bool IsOptional = false,
    bool IsGeneric = false,
    bool IsHidden = false,
    bool IsState = false,
    bool IsPinGroup = false
);

public record VvvvNode(
    string Name,
    string Category,
    string FullName,
    NodeType Type,
    string Summary,
    string Remarks,
    List<string> Tags,
    bool IsGeneric,
    bool HasState,
    List<NodePin> Inputs,
    List<NodePin> Outputs,
    string Source
)
{
    /// <summary>Derived unique key: Source::FullName</summary>
    public string Key => $"{Source}::{FullName}";

    /// <summary>Package/library this node comes from — same as Source.</summary>
    public string Package => Source;
}
