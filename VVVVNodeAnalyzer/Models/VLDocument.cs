using System.Collections.Generic;

namespace VvvvPluginAnalyzer.Models
{
    public class VLDocument
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string DocumentId { get; set; } = "";
        public string LanguageVersion { get; set; } = "";
        public string Version { get; set; } = "";
        public List<NugetDependency> NugetDependencies { get; set; } = new();
        public List<PatchInfo> Patches { get; set; } = new();
        public List<VLNodeDefinition> NodeDefinitions { get; set; } = new(); // New: extracted node definitions
    }

    // Keep existing models for compatibility
    public class PatchInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public CanvasInfo? Canvas { get; set; }
        public List<NodeInfo> Nodes { get; set; } = new();
        public List<PadInfo> Pads { get; set; } = new();
        public List<LinkInfo> Links { get; set; } = new();
        public List<ProcessDefinitionInfo> ProcessDefinitions { get; set; } = new();
    }

    public class CanvasInfo
    {
        public string Id { get; set; } = "";
        public string DefaultCategory { get; set; } = "";
        public string CanvasType { get; set; } = "";
        public bool BordersChecked { get; set; }
    }

    public class NodeInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Bounds { get; set; } = "";
        public NodeReferenceInfo? NodeReference { get; set; }
        public List<PinInfo> Pins { get; set; } = new();
    }

    public class NodeReferenceInfo
    {
        public string LastCategoryFullName { get; set; } = "";
        public string LastDependency { get; set; } = "";
        public List<ChoiceInfo> Choices { get; set; } = new();
        public List<CategoryReferenceInfo> CategoryReferences { get; set; } = new();
    }

    public class ChoiceInfo
    {
        public string Kind { get; set; } = "";
        public string Name { get; set; } = "";
        public bool Fixed { get; set; }
    }

    public class CategoryReferenceInfo
    {
        public string Kind { get; set; } = "";
        public string Name { get; set; } = "";
        public bool NeedsToBeDirectParent { get; set; }
    }

    public class PinInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public bool IsHidden { get; set; }
        public string DefaultValue { get; set; } = "";
        public TypeAnnotationInfo? TypeAnnotation { get; set; }
    }

    public class TypeAnnotationInfo
    {
        public string LastCategoryFullName { get; set; } = "";
        public string LastDependency { get; set; } = "";
        public List<ChoiceInfo> Choices { get; set; } = new();
    }

    public class PadInfo
    {
        public string Id { get; set; } = "";
        public string Comment { get; set; } = "";
        public string Bounds { get; set; } = "";
        public bool ShowValueBox { get; set; }
        public bool IsIOBox { get; set; }
        public string Value { get; set; } = "";
        public TypeAnnotationInfo? TypeAnnotation { get; set; }
    }

    public class LinkInfo
    {
        public string Id { get; set; } = "";
        public string Ids { get; set; } = "";
    }

    public class ProcessDefinitionInfo
    {
        public string Id { get; set; } = "";
        public List<FragmentInfo> Fragments { get; set; } = new();
    }

    public class FragmentInfo
    {
        public string Id { get; set; } = "";
        public string Patch { get; set; } = "";
        public bool Enabled { get; set; }
    }

}