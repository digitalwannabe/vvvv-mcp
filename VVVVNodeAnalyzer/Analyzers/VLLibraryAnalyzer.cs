using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using VvvvPluginAnalyzer.Models;

namespace VvvvPluginAnalyzer.Analyzers
{
    /// <summary>
    /// Parses .vl files (XML) to extract node definitions for the MCP catalog.
    ///
    /// Key VL concepts reflected here:
    /// - VL Document hierarchy: Document → Patches → Operations → Nodes/Pads/Links
    /// - Node types: Process (stateful), Operation (stateless), Record (immutable), Class (mutable)
    /// - Getter/Setter nodes are SYNTHESIZED from Slots — they do not exist as XML methods
    /// - Node full name = "Name (Version) [Category]" — version is optional, in parentheses
    /// - Tags are space-separated (not comma-separated)
    /// - Categories are dot-separated namespaces; canvas DefaultCategory is the local namespace
    /// - .vl library files live in root AND subdirectories (especially src/); help/ must be excluded
    /// </summary>
    public class VLLibraryAnalyzer
    {
        // Help file name prefixes per HelpSystemAnalyzer convention — skip these
        private static readonly string[] HelpFilePrefixes =
            ["HowTo", "Reference ", "Explanation", "Tutorial", "Example", "explanation"];

        // Version pattern: "Name (Version)" e.g. "Split (Count)"
        private static readonly Regex VersionPattern = new(@"^(.+?)\s+\(([^)]+)\)$", RegexOptions.Compiled);

        // Version suffix removal for folder names: ".1.0.3-alpha", ".2.1.2", etc.
        private static readonly Regex FolderVersionPattern =
            new(@"\.\d+(\.\d+)*(-[\w.]+)?$", RegexOptions.Compiled);

        public VLDocument AnalyzeVLLibrary(string pluginDirectory)
        {
            var folderName  = Path.GetFileName(pluginDirectory);
            var packageName = FolderVersionPattern.Replace(folderName, "");

            // Scan ALL .vl files in the package, skipping the help/ directory and
            // files whose names follow help-patch naming conventions.
            var allVlFiles = Directory.GetFiles(pluginDirectory, "*.vl", SearchOption.AllDirectories)
                .Where(f => !IsHelpFile(f, pluginDirectory))
                .OrderBy(f => f)
                .ToList();

            // Bootstrap the VLDocument
            var primaryVlPath = Path.Combine(pluginDirectory, $"{packageName}.vl");
            var vlDoc = new VLDocument
            {
                FilePath  = File.Exists(primaryVlPath) ? primaryVlPath : pluginDirectory,
                FileName  = packageName
            };

            if (File.Exists(primaryVlPath))
            {
                try
                {
                    var primaryDoc = XDocument.Load(primaryVlPath);
                    vlDoc.DocumentId      = primaryDoc.Root?.Attribute("Id")?.Value ?? "";
                    vlDoc.LanguageVersion = primaryDoc.Root?.Attribute("LanguageVersion")?.Value ?? "";
                    vlDoc.Version         = primaryDoc.Root?.Attribute("Version")?.Value ?? "";
                }
                catch { /* metadata is optional */ }
            }

            if (allVlFiles.Count == 0)
                return vlDoc; // Pure .NET package

            var seenDepLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var vlFile in allVlFiles)
            {
                try
                {
                    var doc      = XDocument.Load(vlFile);
                    var fileName = Path.GetFileName(vlFile);

                    // NuGet dependencies (de-duplicated)
                    foreach (var dep in doc.Root?.Elements("NugetDependency") ?? [])
                    {
                        var location = dep.Attribute("Location")?.Value ?? "";
                        if (!string.IsNullOrEmpty(location) && seenDepLocations.Add(location))
                        {
                            vlDoc.NugetDependencies.Add(new NugetDependency
                            {
                                Id       = dep.Attribute("Id")?.Value ?? "",
                                Location = location,
                                Version  = dep.Attribute("Version")?.Value ?? ""
                            });
                        }
                    }

                    // Patch complexity metrics
                    foreach (var patch in doc.Descendants("Patch"))
                        vlDoc.Patches.Add(AnalyzePatch(patch));

                    // Node definitions: read the document-level base category from the
                    // Definitions canvas (CanvasType="FullCategory") if present.
                    var docBaseCategory = ReadDocumentBaseCategory(doc);

                    var mainPatches = doc.Root?.Elements("Patch") ?? [];
                    foreach (var mainPatch in mainPatches)
                    {
                        var defs = ExtractNodeDefinitionsFromPatch(mainPatch, fileName, docBaseCategory);
                        vlDoc.NodeDefinitions.AddRange(defs);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Warning: Could not parse {Path.GetFileName(vlFile)}: {ex.Message}");
                }
            }

            return vlDoc;
        }

        // ─── File filtering ────────────────────────────────────────────────────

        private static bool IsHelpFile(string filePath, string packageRoot)
        {
            // Exclude anything inside a "help" subdirectory
            var relative = Path.GetRelativePath(packageRoot, filePath);
            var parts    = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Any(p => p.Equals("help", StringComparison.OrdinalIgnoreCase)))
                return true;

            // Exclude files whose names start with help-patch prefixes
            var baseName = Path.GetFileNameWithoutExtension(filePath);
            return HelpFilePrefixes.Any(prefix =>
                baseName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        // ─── Document-level base category ─────────────────────────────────────

        /// <summary>
        /// Reads the document's base category from the root Canvas that has
        /// CanvasType="FullCategory" and a DefaultCategory attribute.
        /// This is what the Definitions patch sets as the root namespace.
        /// </summary>
        private static string? ReadDocumentBaseCategory(XDocument doc)
        {
            foreach (var canvas in doc.Descendants("Canvas"))
            {
                if (canvas.Attribute("CanvasType")?.Value == "FullCategory")
                {
                    var cat = canvas.Attribute("DefaultCategory")?.Value;
                    if (!string.IsNullOrEmpty(cat)) return cat;
                }
            }
            return null;
        }

        // ─── Patch → node definitions ──────────────────────────────────────────

        private List<VLNodeDefinition> ExtractNodeDefinitionsFromPatch(
            XElement patch, string sourceDocument, string? docBaseCategory)
        {
            var nodeDefinitions = new List<VLNodeDefinition>();
            var canvas = patch.Element("Canvas");
            if (canvas == null) return nodeDefinitions;

            // The root canvas (FullCategory) has no Name — start with doc base category.
            // Nested canvases with Name attributes build the dot-separated category path.
            var rootCategory = canvas.Attribute("DefaultCategory")?.Value ?? docBaseCategory ?? "";
            ScanCanvas(canvas, rootCategory, sourceDocument, nodeDefinitions);
            return nodeDefinitions;
        }

        /// <summary>
        /// Recursively scans the canvas tree.
        ///
        /// Category building rules (from the gray book):
        ///   1. A Canvas with a <c>Name</c> attribute extends the parent path:
        ///      parent "3D" + canvas Name "Transform" → "3D.Transform"
        ///   2. A Canvas with a <c>DefaultCategory</c> attribute overrides the path entirely.
        ///   3. A Canvas with neither inherits the parent category unchanged.
        ///
        /// This matches how vvvv's own Definitions patch hierarchy works.
        /// </summary>
        private void ScanCanvas(XElement canvas, string currentCategory,
            string sourceDocument, List<VLNodeDefinition> nodeDefinitions)
        {
            foreach (var defNode in canvas.Elements("Node").Where(IsDefinitionNode))
            {
                try
                {
                    var nodeDef = ExtractNodeDefinition(defNode, currentCategory, sourceDocument);
                    if (nodeDef != null)
                        nodeDefinitions.Add(nodeDef);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not extract '{defNode.Attribute("Name")?.Value}': {ex.Message}");
                }
            }

            foreach (var nested in canvas.Elements("Canvas"))
            {
                string childCategory;

                // DefaultCategory explicitly sets the full category for this canvas
                var defaultCat = nested.Attribute("DefaultCategory")?.Value;
                if (!string.IsNullOrEmpty(defaultCat))
                {
                    childCategory = defaultCat;
                }
                // Name attribute extends the parent path: "parent.Name"
                else
                {
                    var canvasName = nested.Attribute("Name")?.Value;
                    childCategory = (!string.IsNullOrEmpty(canvasName))
                        ? (string.IsNullOrEmpty(currentCategory)
                               ? canvasName
                               : $"{currentCategory}.{canvasName}")
                        : currentCategory;
                }

                ScanCanvas(nested, childCategory, sourceDocument, nodeDefinitions);
            }
        }

        private static bool IsDefinitionNode(XElement node)
        {
            var nodeRef = node.Element(XName.Get("NodeReference", "property"));
            if (nodeRef == null) return false;

            return nodeRef.Elements("Choice")
                .Select(c => c.Attribute("Kind")?.Value)
                .Any(kind => kind is "RecordDefinition" or "ClassDefinition"
                                  or "ContainerDefinition" or "OperationDefinition"
                                  or "InterfaceDefinition" or "ForwardDefinition");
        }

        // ─── Node definition extraction ────────────────────────────────────────

        private VLNodeDefinition? ExtractNodeDefinition(XElement defNode,
            string inheritedCategory, string sourceDocument)
        {
            var rawName = defNode.Attribute("Name")?.Value;
            if (string.IsNullOrEmpty(rawName)) return null;

            // Parse version from "Name (Version)" pattern
            var (baseName, version) = ParseNameVersion(rawName);

            var nodeRef  = defNode.Element(XName.Get("NodeReference", "property"));
            var choices  = nodeRef?.Elements("Choice").ToList() ?? [];
            var nodeType = DetermineNodeType(choices);
            var category = DetermineCategory(nodeRef, inheritedCategory);

            var nodeDef = new VLNodeDefinition
            {
                Name    = baseName,
                Version = version,
                Category = category,
                Type    = nodeType,
                Source  = sourceDocument,
                Summary = defNode.Attribute("Summary")?.Value ?? "",
                Remarks = defNode.Attribute("Remarks")?.Value ?? "",
                Tags    = ParseTags(defNode.Attribute("Tags")?.Value ?? "")
            };

            var nestedPatch = defNode.Elements("Patch").FirstOrDefault();
            if (nestedPatch != null)
                ExtractDefinitionDetails(nestedPatch, nodeDef);

            return nodeDef;
        }

        /// <summary>
        /// Parses "Split (Count)" → ("Split", "Count").
        /// Returns (name, "") when no version label is present.
        /// </summary>
        private static (string name, string version) ParseNameVersion(string raw)
        {
            var m = VersionPattern.Match(raw);
            return m.Success ? (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()) : (raw, "");
        }

        private static VLNodeType DetermineNodeType(List<XElement> choices)
        {
            foreach (var choice in choices)
            {
                switch (choice.Attribute("Kind")?.Value)
                {
                    case "RecordDefinition":    return VLNodeType.Record;
                    case "ClassDefinition":     return VLNodeType.Class;
                    case "ContainerDefinition": return VLNodeType.Process;
                    case "OperationDefinition": return VLNodeType.Operation;
                    case "InterfaceDefinition": return VLNodeType.Interface;
                    case "ForwardDefinition":   return VLNodeType.Forward;
                }
            }
            return VLNodeType.Unknown;
        }

        private static string DetermineCategory(XElement? nodeRef, string inheritedCategory)
        {
            // 1. LastCategoryFullName — set by vvvv after saving; most reliable when present.
            //    Skip "Primitive" as it is a placeholder, not a real user category.
            var full = nodeRef?.Attribute("LastCategoryFullName")?.Value;
            if (!string.IsNullOrEmpty(full) && full != "Primitive") return full;

            // 2. Explicit CategoryReference (e.g. on operation calls, not definitions)
            var catRef  = nodeRef?.Element("CategoryReference");
            var catName = catRef?.Attribute("Name")?.Value;
            if (!string.IsNullOrEmpty(catName) && catName != "Primitive") return catName;

            // 3. Inherited from the canvas Name hierarchy (primary path for definitions)
            if (!string.IsNullOrEmpty(inheritedCategory)) return inheritedCategory;

            // 4. Genuine unknown — this should be rare with proper canvas traversal
            return "Unknown";
        }

        // ─── Definition details (slots, methods, pins) ─────────────────────────

        private void ExtractDefinitionDetails(XElement patch, VLNodeDefinition nodeDef)
        {
            nodeDef.IsGeneric = patch.Attribute("IsGeneric")?.Value == "true";

            // Slots (fields/properties of the type)
            foreach (var slot in patch.Elements("Slot"))
            {
                nodeDef.Slots.Add(new VLSlot
                {
                    Name         = slot.Attribute("Name")?.Value ?? "",
                    Summary      = slot.Attribute("Summary")?.Value ?? "",
                    DefaultValue = slot.Attribute("DefaultValue")?.Value
                                   ?? slot.Element(XName.Get("Value", "property"))?.Value ?? "",
                    TypeInfo     = ExtractTypeInfo(slot.Element(XName.Get("TypeAnnotation", "property")))
                });
            }

            // Named sub-patches = operations (Create, Update, Dispose, custom)
            foreach (var opPatch in patch.Elements("Patch")
                         .Where(p => !string.IsNullOrEmpty(p.Attribute("Name")?.Value)))
            {
                var method = ExtractMethodFromPatch(opPatch);
                if (method != null) nodeDef.Methods.Add(method);
            }

            var processDefinition = patch.Element("ProcessDefinition");
            if (processDefinition != null)
            {
                AnalyzeProcessDefinition(processDefinition, nodeDef);
            }
            else if (nodeDef.Methods.Any())
            {
                nodeDef.ActiveMethods.AddRange(nodeDef.Methods);
            }
            else
            {
                // Operation whose pins are direct children of the main patch
                ExtractDirectPins(patch, nodeDef);
                return; // pins already set
            }

            GenerateFinalPins(nodeDef);
        }

        private void ExtractDirectPins(XElement patch, VLNodeDefinition nodeDef)
        {
            nodeDef.IsGeneric = nodeDef.IsGeneric || patch.Attribute("IsGeneric")?.Value == "true";
            foreach (var pin in patch.Elements("Pin"))
            {
                var pinInfo = BuildPin(pin);
                if (pinInfo.IsHidden) continue;
                if (pinInfo.Kind.Contains("Input"))  nodeDef.InputPins.Add(pinInfo);
                else if (pinInfo.Kind.Contains("Output")) nodeDef.OutputPins.Add(pinInfo);
            }
        }

        private VLMethod? ExtractMethodFromPatch(XElement patch)
        {
            var name = patch.Attribute("Name")?.Value;
            if (string.IsNullOrEmpty(name)) return null;

            var method = new VLMethod
            {
                Id      = patch.Attribute("Id")?.Value ?? "",
                Name    = name,
                Summary = patch.Attribute("Summary")?.Value ?? "",
                Remarks = patch.Attribute("Remarks")?.Value ?? "",
                Tags    = ParseTags(patch.Attribute("Tags")?.Value ?? "")
            };

            foreach (var pin in patch.Elements("Pin"))
            {
                var p = BuildPin(pin);
                if (p.Kind.Contains("Input"))  method.InputPins.Add(p);
                else if (p.Kind.Contains("Output")) method.OutputPins.Add(p);
            }

            return method;
        }

        private static VLPin BuildPin(XElement pin)
        {
            var visibility = pin.Attribute("Visibility")?.Value ?? "";
            return new VLPin
            {
                Id           = pin.Attribute("Id")?.Value ?? "",
                Name         = pin.Attribute("Name")?.Value ?? "",
                Kind         = pin.Attribute("Kind")?.Value ?? "",
                IsHidden     = pin.Attribute("IsHidden")?.Value == "true"
                               || visibility.Equals("Hidden", StringComparison.OrdinalIgnoreCase),
                IsOptional   = visibility.Equals("Optional", StringComparison.OrdinalIgnoreCase)
                               || visibility.Equals("OnlyInspector", StringComparison.OrdinalIgnoreCase),
                DefaultValue = pin.Attribute("DefaultValue")?.Value ?? "",
                Summary      = pin.Attribute("Summary")?.Value ?? "",
                TypeInfo     = ExtractTypeInfo(pin.Element(XName.Get("TypeAnnotation", "property")))
            };
        }

        private void AnalyzeProcessDefinition(XElement processDefinition, VLNodeDefinition nodeDefinition)
        {
            nodeDefinition.HasStateOut = processDefinition.Attribute("HasStateOut")?.Value == "true";
            nodeDefinition.Aspects     = processDefinition.Attribute("Aspects")?.Value ?? "";

            var activeIds = new HashSet<string>();
            foreach (var fragment in processDefinition.Elements("Fragment"))
            {
                var patchId = fragment.Attribute("Patch")?.Value;
                var enabled = fragment.Attribute("Enabled")?.Value != "false";
                if (!string.IsNullOrEmpty(patchId) && enabled) activeIds.Add(patchId);
            }

            foreach (var method in nodeDefinition.Methods)
            {
                if (activeIds.Contains(method.Id))
                {
                    method.IsPartOfProcessDefinition = true;
                    nodeDefinition.ActiveMethods.Add(method);
                }
            }
        }

        /// <summary>
        /// Generates the public pin interface for a node definition.
        ///
        /// Record / Class:
        ///   - Getter/setter nodes are SYNTHESIZED from Slots. vvvv generates these at
        ///     runtime; they do not appear as explicit methods in the VL XML.
        ///   - The pins stored on the definition itself reflect the Create constructor.
        ///
        /// Process:
        ///   - Pins come from the Update operation (primary user-facing operation).
        ///   - Fallback to all active methods if no Update is found.
        ///
        /// Operation:
        ///   - Pins come from all active methods (usually one).
        /// </summary>
        private static void GenerateFinalPins(VLNodeDefinition nodeDef)
        {
            var inputPins  = new Dictionary<string, VLPin>(StringComparer.Ordinal);
            var outputPins = new Dictionary<string, VLPin>(StringComparer.Ordinal);

            if (nodeDef.Type == VLNodeType.Record || nodeDef.Type == VLNodeType.Class)
            {
                // For the constructor node: inputs = slots, output = instance.
                // Getter/setter nodes are handled separately in UsableNodeExtractor.
                foreach (var slot in nodeDef.Slots)
                {
                    if (string.IsNullOrEmpty(slot.Name)) continue;
                    inputPins[slot.Name] = new VLPin
                    {
                        Name         = slot.Name,
                        Kind         = "InputPin",
                        Summary      = slot.Summary,
                        DefaultValue = slot.DefaultValue,
                        TypeInfo     = slot.TypeInfo
                    };
                }
            }
            else
            {
                // Process: prefer Update pins; fall back to all active methods.
                // Operation: use all active / all methods.
                var updateMethod = nodeDef.ActiveMethods.FirstOrDefault(m =>
                    m.Name.Equals("Update", StringComparison.OrdinalIgnoreCase));
                var relevantMethods = (nodeDef.Type == VLNodeType.Process && updateMethod != null)
                    ? [updateMethod]
                    : (nodeDef.ActiveMethods.Any() ? nodeDef.ActiveMethods : nodeDef.Methods);

                foreach (var method in relevantMethods)
                {
                    foreach (var pin in method.InputPins.Where(p => !p.IsHidden))
                        inputPins[pin.Name] = pin;
                    foreach (var pin in method.OutputPins.Where(p => !p.IsHidden))
                        outputPins[pin.Name] = pin;
                }
            }

            nodeDef.InputPins  = inputPins.Values.ToList();
            nodeDef.OutputPins = outputPins.Values.ToList();
        }

        // ─── Type info extraction ──────────────────────────────────────────────

        private static VLTypeInfo? ExtractTypeInfo(XElement? typeAnnotation)
        {
            if (typeAnnotation == null) return null;

            var typeInfo = new VLTypeInfo
            {
                Category   = typeAnnotation.Attribute("LastCategoryFullName")?.Value ?? "",
                Dependency = typeAnnotation.Attribute("LastDependency")?.Value ?? ""
            };

            foreach (var choice in typeAnnotation.Elements("Choice"))
            {
                typeInfo.Choices.Add(new VLTypeChoice
                {
                    Kind  = choice.Attribute("Kind")?.Value ?? "",
                    Name  = choice.Attribute("Name")?.Value ?? "",
                    Fixed = choice.Attribute("Fixed")?.Value == "true"
                });
            }

            var typeChoice = typeInfo.Choices.FirstOrDefault(c =>
                c.Kind.Contains("Type") || c.Kind.Contains("Flag"));

            if (typeChoice != null)
            {
                var baseName    = typeChoice.Name;
                var typeArgs    = typeAnnotation.Element(XName.Get("TypeArguments", "property"));
                if (typeArgs != null)
                {
                    var args = ExtractTypeArguments(typeArgs);
                    typeInfo.TypeArguments = args;
                    typeInfo.TypeName  = args.Count > 0 ? $"{baseName}<{string.Join(",", args)}>" : baseName;
                    typeInfo.IsGeneric = args.Count > 0;
                }
                else
                {
                    typeInfo.TypeName = baseName;
                }
            }

            return typeInfo;
        }

        private static List<string> ExtractTypeArguments(XElement typeArguments)
        {
            return typeArguments.Elements("TypeReference")
                .Select(ExtractTypeFromReference)
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();
        }

        private static string ExtractTypeFromReference(XElement typeRef)
        {
            var choice = typeRef.Elements("Choice").FirstOrDefault(c =>
                c.Attribute("Kind")?.Value?.Contains("Type") == true ||
                c.Attribute("Kind")?.Value?.Contains("Flag") == true);

            if (choice == null) return "Object";

            var baseName  = choice.Attribute("Name")?.Value ?? "Object";
            var nestedArgs = typeRef.Element(XName.Get("TypeArguments", "property"));
            if (nestedArgs != null)
            {
                var nested = ExtractTypeArguments(nestedArgs);
                if (nested.Count > 0) return $"{baseName}<{string.Join(",", nested)}>";
            }
            return baseName;
        }

        // ─── Tags ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Tags in VL are space-separated lowercase terms (per Design Guidelines).
        /// Example: "math filter 2d"
        /// </summary>
        private static List<string> ParseTags(string tagsString)
        {
            if (string.IsNullOrEmpty(tagsString)) return [];
            return tagsString.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                             .Select(t => t.Trim().ToLowerInvariant())
                             .Where(t => t.Length > 0)
                             .Distinct()
                             .ToList();
        }

        // ─── Patch complexity (unchanged, kept for metrics) ───────────────────

        private static PatchInfo AnalyzePatch(XElement patchElement)
        {
            var patch = new PatchInfo
            {
                Id   = patchElement.Attribute("Id")?.Value ?? "",
                Name = patchElement.Attribute("Name")?.Value ?? "Main"
            };

            var canvas = patchElement.Element("Canvas");
            if (canvas != null)
            {
                patch.Canvas = new CanvasInfo
                {
                    Id              = canvas.Attribute("Id")?.Value ?? "",
                    DefaultCategory = canvas.Attribute("DefaultCategory")?.Value ?? "",
                    CanvasType      = canvas.Attribute("CanvasType")?.Value ?? "",
                    BordersChecked  = bool.TryParse(canvas.Attribute("BordersChecked")?.Value, out var b) && b
                };
                patch.Nodes = canvas.Elements("Node").Select(n => new NodeInfo
                    { Id = n.Attribute("Id")?.Value ?? "", Name = n.Attribute("Name")?.Value ?? "" }).ToList();
                patch.Pads = canvas.Elements("Pad").Select(p => new PadInfo
                    { Id = p.Attribute("Id")?.Value ?? "", Comment = p.Attribute("Comment")?.Value ?? "" }).ToList();
            }

            patch.Links = patchElement.Elements("Link").Select(l => new LinkInfo
                { Id = l.Attribute("Id")?.Value ?? "", Ids = l.Attribute("Ids")?.Value ?? "" }).ToList();

            return patch;
        }
    }
}
