using System.Collections.Generic;

namespace VvvvPluginAnalyzer.Models
{
    public class VLNodeDefinition
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public VLNodeType Type { get; set; }
        public string Summary { get; set; } = "";
        public string Remarks { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public bool IsGeneric { get; set; }
        public bool HasStateOut { get; set; }
        public string Aspects { get; set; } = "";
        public string Source { get; set; } = "";
        
        // Structure elements
        public List<VLSlot> Slots { get; set; } = new();
        public List<VLMethod> Methods { get; set; } = new();
        public List<VLMethod> ActiveMethods { get; set; } = new();
        
        // Final interface pins (computed from slots and active methods)
        public List<VLPin> InputPins { get; set; } = new();
        public List<VLPin> OutputPins { get; set; } = new();
    }

    public enum VLNodeType
    {
        Unknown,
        Record,
        Class,
        Process,
        Operation
    }

    public class VLSlot
    {
        public string Name { get; set; } = "";
        public string Summary { get; set; } = "";
        public VLTypeInfo? TypeInfo { get; set; }
    }

    public class VLMethod
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Remarks { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public bool IsPartOfProcessDefinition { get; set; }
        public List<VLPin> InputPins { get; set; } = new();
        public List<VLPin> OutputPins { get; set; } = new();
    }

    public class VLPin
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public bool IsHidden { get; set; }
        public string DefaultValue { get; set; } = "";
        public string Summary { get; set; } = "";
        public VLTypeInfo? TypeInfo { get; set; }
    }

    public class VLTypeInfo
    {
        public string Category { get; set; } = "";
        public string Dependency { get; set; } = "";
        public string TypeName { get; set; } = "";
        public bool IsGeneric { get; set; }
        public List<VLTypeChoice> Choices { get; set; } = new();
        public List<string> TypeArguments { get; set; } = new(); // Store the resolved type arguments
    }

    public class VLTypeChoice
    {
        public string Kind { get; set; } = "";
        public string Name { get; set; } = "";
        public bool Fixed { get; set; }
    }
}