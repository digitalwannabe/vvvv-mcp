using System;
using System.Collections.Generic;

namespace VvvvPluginAnalyzer.Models
{
    public class DotNetLibraryInfo
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string AssemblyName { get; set; } = "";
        public string Version { get; set; } = "";
        public string TargetFramework { get; set; } = "";
        public bool IsManaged { get; set; }
        public bool HasXmlDocumentation { get; set; }
        public string? XmlDocumentationPath { get; set; }
        public List<string> ReferencedAssemblies { get; set; } = new();
        public List<string> Namespaces { get; set; } = new();
        public List<DotNetTypeInfo> PublicTypes { get; set; } = new();
        public List<DotNetNodeInfo> AvailableNodes { get; set; } = new();
    }

    public class DotNetTypeInfo
    {
        public string Name { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Namespace { get; set; } = "";
        public bool IsPublic { get; set; }
        public bool IsStatic { get; set; }
        public bool IsAbstract { get; set; }
        public string TypeKind { get; set; } = ""; // Class, Interface, Struct, Enum, etc.
        public string? XmlDocumentation { get; set; }
        public List<DotNetMethodInfo> PublicMethods { get; set; } = new();
        public List<DotNetPropertyInfo> PublicProperties { get; set; } = new();
    }

    public class DotNetMethodInfo
    {
        public string Name { get; set; } = "";
        public string ReturnType { get; set; } = "";
        public bool IsStatic { get; set; }
        public bool IsPublic { get; set; }
        public bool IsExtensionMethod { get; set; }
        public string? XmlDocumentation { get; set; }
        public List<DotNetParameterInfo> Parameters { get; set; } = new();
    }

    public class DotNetParameterInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public bool HasDefaultValue { get; set; }
        public string? DefaultValue { get; set; }
        public bool IsOptional { get; set; }
        public bool IsOut { get; set; }
        public bool IsRef { get; set; }
    }

    public class DotNetPropertyInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public bool HasGetter { get; set; }
        public bool HasSetter { get; set; }
        public bool IsStatic { get; set; }
        public string? XmlDocumentation { get; set; }
    }

    public class DotNetNodeInfo
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string FullName { get; set; } = "";
        public string NodeType { get; set; } = ""; // Method, Property, Constructor
        public bool IsStatic { get; set; }
        public string DeclaringType { get; set; } = "";
        public string? Documentation { get; set; }
        public List<NodePin> InputPins { get; set; } = new();
        public List<NodePin> OutputPins { get; set; } = new();
    }

    public class NodePin
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public bool IsOptional { get; set; }
        public string? DefaultValue { get; set; }
        public string? Documentation { get; set; }
    }
}