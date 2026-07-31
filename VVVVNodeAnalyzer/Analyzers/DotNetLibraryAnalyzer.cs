using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using VvvvPluginAnalyzer.Models;

namespace VvvvPluginAnalyzer.Analyzers
{
    public class DotNetLibraryAnalyzer
    {
        private readonly List<string> _extraSearchDirs;

        /// <param name="extraSearchDirs">
        /// Additional directories to probe when resolving assembly dependencies
        /// (e.g. the vvvv install root, the packs/dependencies folder).
        /// </param>
        public DotNetLibraryAnalyzer(IEnumerable<string>? extraSearchDirs = null)
        {
            _extraSearchDirs = extraSearchDirs?.ToList() ?? new List<string>();
        }

        public List<DotNetLibraryInfo> AnalyzeLibraries(string pluginDirectory)
        {
            var libraries = new List<DotNetLibraryInfo>();
            var libDir = Path.Combine(pluginDirectory, "lib");

            if (!Directory.Exists(libDir))
                return libraries;

            // Find all .dll files recursively
            var dllFiles = Directory.GetFiles(libDir, "*.dll", SearchOption.AllDirectories);

            foreach (var dllFile in dllFiles)
            {
                try
                {
                    var libInfo = AnalyzeLibrary(dllFile, pluginDirectory);
                    if (libInfo != null)
                        libraries.Add(libInfo);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not analyze {dllFile}: {ex.Message}");
                }
            }

            return libraries;
        }

        private DotNetLibraryInfo? AnalyzeLibrary(string dllPath, string pluginDirectory)
        {
            // Build search paths for the MetadataLoadContext resolver:
            //   1. The directory that contains the DLL itself
            //   2. The vvvv root and any other caller-supplied dirs
            //   3. The .NET runtime directory (for System.*, mscorlib, etc.)
            var searchDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.GetDirectoryName(dllPath)!,
            };
            foreach (var d in _extraSearchDirs)
                searchDirs.Add(d);
            searchDirs.Add(RuntimeEnvironment.GetRuntimeDirectory());

            // Collect all DLLs in those directories to form the resolver universe
            var resolverPaths = searchDirs
                .Where(Directory.Exists)
                .SelectMany(d => Directory.GetFiles(d, "*.dll"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            try
            {
                using var mlc = new MetadataLoadContext(new PathAssemblyResolver(resolverPaths));
                var assembly = mlc.LoadFromAssemblyPath(dllPath);

                var libInfo = new DotNetLibraryInfo
                {
                    FilePath = dllPath,
                    FileName = Path.GetFileName(dllPath),
                    AssemblyName = assembly.GetName().Name ?? "",
                    Version = assembly.GetName().Version?.ToString() ?? "",
                    IsManaged = true,
                    TargetFramework = "Unknown" // attribute lookup not supported in MetadataLoadContext
                };

                // Referenced assemblies
                libInfo.ReferencedAssemblies = assembly.GetReferencedAssemblies()
                    .Select(a => a.Name ?? "Unknown")
                    .ToList();

                // XML documentation
                var xmlDocPath = Path.ChangeExtension(dllPath, ".xml");
                if (File.Exists(xmlDocPath))
                {
                    libInfo.XmlDocumentationPath = xmlDocPath;
                    libInfo.HasXmlDocumentation = true;
                }

                var xmlDoc = LoadXmlDocumentation(xmlDocPath);

                // GetExportedTypes may still fail for individual types — collect what we can
                Type[] types;
                try
                {
                    types = assembly.GetExportedTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray()!;
                }
                catch
                {
                    types = Array.Empty<Type>();
                }

                foreach (var type in types)
                {
                    try
                    {
                        if (type.IsPublic)
                        {
                            var typeInfo = AnalyzeType(type, xmlDoc);
                            libInfo.PublicTypes.Add(typeInfo);

                            var nodes = ExtractNodesFromType(type, xmlDoc);
                            libInfo.AvailableNodes.AddRange(nodes);
                        }
                    }
                    catch { /* skip individual type failures */ }
                }

                libInfo.Namespaces = libInfo.PublicTypes
                    .Select(t => t.Namespace)
                    .Where(ns => !string.IsNullOrEmpty(ns))
                    .Distinct()
                    .OrderBy(ns => ns)
                    .ToList();

                return libInfo;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error analyzing {dllPath}: {ex.Message}");
                return null;
            }
        }

        private XDocument? LoadXmlDocumentation(string? xmlPath)
        {
            if (string.IsNullOrEmpty(xmlPath) || !File.Exists(xmlPath))
                return null;
            try { return XDocument.Load(xmlPath); }
            catch { return null; }
        }

        private DotNetTypeInfo AnalyzeType(Type type, XDocument? xmlDoc)
        {
            var typeInfo = new DotNetTypeInfo
            {
                Name = type.Name,
                FullName = type.FullName ?? type.Name,
                Namespace = type.Namespace ?? "",
                IsPublic = type.IsPublic,
                IsStatic = type.IsAbstract && type.IsSealed,
                IsAbstract = type.IsAbstract && !type.IsSealed,
                TypeKind = GetTypeKind(type)
            };

            typeInfo.XmlDocumentation = GetXmlDocumentation(xmlDoc, $"T:{type.FullName}");

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                .Where(m => !m.IsSpecialName && m.DeclaringType == type);
            foreach (var method in methods)
                typeInfo.PublicMethods.Add(AnalyzeMethod(method, xmlDoc));

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                .Where(p => p.DeclaringType == type);
            foreach (var property in properties)
                typeInfo.PublicProperties.Add(AnalyzeProperty(property, xmlDoc));

            return typeInfo;
        }

        private string GetTypeKind(Type type)
        {
            if (type.IsEnum) return "Enum";
            if (type.IsInterface) return "Interface";
            if (type.IsValueType) return "Struct";
            if (type.IsClass) return "Class";
            return "Unknown";
        }

        private DotNetMethodInfo AnalyzeMethod(MethodInfo method, XDocument? xmlDoc)
        {
            var methodInfo = new DotNetMethodInfo
            {
                Name = method.Name,
                ReturnType = GetFriendlyTypeName(method.ReturnType),
                IsStatic = method.IsStatic,
                IsPublic = method.IsPublic,
                IsExtensionMethod = method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false)
            };

            var xmlKey = GenerateXmlDocKey(method);
            methodInfo.XmlDocumentation = GetXmlDocumentation(xmlDoc, xmlKey);

            foreach (var param in method.GetParameters())
            {
                var paramInfo = new DotNetParameterInfo
                {
                    Name = param.Name ?? "",
                    Type = GetFriendlyTypeName(param.ParameterType),
                    HasDefaultValue = param.HasDefaultValue,
                    DefaultValue = param.HasDefaultValue ? param.DefaultValue?.ToString() : null,
                    IsOptional = param.IsOptional,
                    IsOut = param.IsOut,
                    IsRef = param.ParameterType.IsByRef && !param.IsOut
                };
                methodInfo.Parameters.Add(paramInfo);
            }

            return methodInfo;
        }

        private DotNetPropertyInfo AnalyzeProperty(PropertyInfo property, XDocument? xmlDoc)
        {
            var propInfo = new DotNetPropertyInfo
            {
                Name = property.Name,
                Type = GetFriendlyTypeName(property.PropertyType),
                HasGetter = property.CanRead,
                HasSetter = property.CanWrite,
                IsStatic = property.GetMethod?.IsStatic ?? property.SetMethod?.IsStatic ?? false
            };

            var xmlKey = $"P:{property.DeclaringType?.FullName}.{property.Name}";
            propInfo.XmlDocumentation = GetXmlDocumentation(xmlDoc, xmlKey);
            return propInfo;
        }

        private List<DotNetNodeInfo> ExtractNodesFromType(Type type, XDocument? xmlDoc)
        {
            var nodes = new List<DotNetNodeInfo>();

            var staticMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => !m.IsSpecialName && m.DeclaringType == type);
            foreach (var method in staticMethods)
                nodes.Add(CreateNodeFromMethod(method, type, xmlDoc, true));

            if (!type.IsAbstract || !type.IsSealed)
            {
                var instanceMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => !m.IsSpecialName && m.DeclaringType == type &&
                               !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_"));
                foreach (var method in instanceMethods)
                    nodes.Add(CreateNodeFromMethod(method, type, xmlDoc, false));

                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Where(p => p.DeclaringType == type);
                foreach (var property in properties)
                    nodes.Add(CreateNodeFromProperty(property, type, xmlDoc));
            }

            return nodes;
        }

        private DotNetNodeInfo CreateNodeFromMethod(MethodInfo method, Type declaringType, XDocument? xmlDoc, bool isStatic)
        {
            var node = new DotNetNodeInfo
            {
                Name = method.Name,
                Category = $"{declaringType.Namespace}.{declaringType.Name}",
                FullName = $"{declaringType.FullName}.{method.Name}",
                NodeType = "Method",
                IsStatic = isStatic,
                DeclaringType = declaringType.FullName ?? declaringType.Name
            };

            var xmlKey = GenerateXmlDocKey(method);
            node.Documentation = GetXmlDocumentation(xmlDoc, xmlKey);

            if (!isStatic)
            {
                node.InputPins.Add(new NodePin
                {
                    Name = "Input",
                    Type = GetFriendlyTypeName(declaringType),
                    IsOptional = false,
                    Documentation = "Instance of the object"
                });
            }

            foreach (var param in method.GetParameters())
            {
                var pin = new NodePin
                {
                    Name = param.Name ?? "Parameter",
                    Type = GetFriendlyTypeName(param.ParameterType),
                    IsOptional = param.IsOptional || param.HasDefaultValue,
                    DefaultValue = param.HasDefaultValue ? param.DefaultValue?.ToString() : null
                };

                if (param.IsOut)
                    node.OutputPins.Add(pin);
                else
                    node.InputPins.Add(pin);
            }

            if (method.ReturnType.FullName != "System.Void")
            {
                node.OutputPins.Add(new NodePin
                {
                    Name = "Output",
                    Type = GetFriendlyTypeName(method.ReturnType),
                    IsOptional = false,
                    Documentation = "Return value"
                });
            }

            return node;
        }

        private DotNetNodeInfo CreateNodeFromProperty(PropertyInfo property, Type declaringType, XDocument? xmlDoc)
        {
            var node = new DotNetNodeInfo
            {
                Name = property.Name,
                Category = $"{declaringType.Namespace}.{declaringType.Name}",
                FullName = $"{declaringType.FullName}.{property.Name}",
                NodeType = "Property",
                IsStatic = property.GetMethod?.IsStatic ?? property.SetMethod?.IsStatic ?? false,
                DeclaringType = declaringType.FullName ?? declaringType.Name
            };

            var xmlKey = $"P:{declaringType.FullName}.{property.Name}";
            node.Documentation = GetXmlDocumentation(xmlDoc, xmlKey);

            if (!node.IsStatic)
            {
                node.InputPins.Add(new NodePin
                {
                    Name = "Input",
                    Type = GetFriendlyTypeName(declaringType),
                    IsOptional = false,
                    Documentation = "Instance of the object"
                });
            }

            if (property.CanWrite)
            {
                node.InputPins.Add(new NodePin
                {
                    Name = "Value",
                    Type = GetFriendlyTypeName(property.PropertyType),
                    IsOptional = false,
                    Documentation = "Value to set"
                });
            }

            if (property.CanRead)
            {
                node.OutputPins.Add(new NodePin
                {
                    Name = "Output",
                    Type = GetFriendlyTypeName(property.PropertyType),
                    IsOptional = false,
                    Documentation = "Property value"
                });
            }

            return node;
        }

        private string GenerateXmlDocKey(MethodInfo method)
        {
            var parameters = method.GetParameters();
            var paramTypes = parameters.Select(p => p.ParameterType.FullName).ToArray();
            var paramString = paramTypes.Length > 0 ? $"({string.Join(",", paramTypes)})" : "";
            return $"M:{method.DeclaringType?.FullName}.{method.Name}{paramString}";
        }

        private string? GetXmlDocumentation(XDocument? xmlDoc, string key)
        {
            if (xmlDoc == null) return null;
            var member = xmlDoc.Root?.Element("members")?.Elements("member")
                .FirstOrDefault(m => m.Attribute("name")?.Value == key);
            var summary = member?.Element("summary")?.Value?.Trim();
            return string.IsNullOrEmpty(summary) ? null : summary;
        }

        /// <summary>
        /// Returns a human-readable type name. Uses full-name string comparisons
        /// because types from MetadataLoadContext cannot be compared with == to
        /// types from the runtime context.
        /// </summary>
        private string GetFriendlyTypeName(Type type)
        {
            var fullName = type.FullName ?? type.Name;

            // Strip by-ref marker
            if (type.IsByRef)
                return GetFriendlyTypeName(type.GetElementType()!) + "&";

            return fullName switch
            {
                "System.Void"    => "void",
                "System.Int32"   => "int",
                "System.String"  => "string",
                "System.Boolean" => "bool",
                "System.Single"  => "float",
                "System.Double"  => "double",
                "System.Decimal" => "decimal",
                "System.Int64"   => "long",
                "System.Int16"   => "short",
                "System.Byte"    => "byte",
                "System.Char"    => "char",
                "System.Object"  => "object",
                _ => BuildGenericOrArrayName(type)
            };
        }

        private string BuildGenericOrArrayName(Type type)
        {
            if (type.IsArray)
                return $"{GetFriendlyTypeName(type.GetElementType()!)}[]";

            if (type.IsGenericType)
            {
                var backtick = type.Name.IndexOf('`');
                var baseName = backtick >= 0 ? type.Name[..backtick] : type.Name;
                var args = type.GetGenericArguments().Select(GetFriendlyTypeName);
                return $"{baseName}<{string.Join(", ", args)}>";
            }

            return type.Name;
        }
    }
}
