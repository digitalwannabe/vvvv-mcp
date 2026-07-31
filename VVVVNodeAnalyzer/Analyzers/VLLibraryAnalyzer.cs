using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using VvvvPluginAnalyzer.Models;

namespace VvvvPluginAnalyzer.Analyzers
{
    public class VLLibraryAnalyzer
    {

        public VLDocument AnalyzeVLLibrary(string pluginDirectory)
        {
            // Get the folder name and remove version suffix
            var folderName = Path.GetFileName(pluginDirectory);
            var packageName = RemoveVersionSuffix(folderName);

            // Collect all root-level .vl files (not in subdirectories)
            var allVlFiles = Directory.GetFiles(pluginDirectory, "*.vl", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f)
                .ToList();

            // Bootstrap the VLDocument, preferring the primary file for metadata
            var primaryVlPath = Path.Combine(pluginDirectory, $"{packageName}.vl");
            var vlDoc = new VLDocument
            {
                FilePath = File.Exists(primaryVlPath) ? primaryVlPath : pluginDirectory,
                FileName = packageName
            };

            if (File.Exists(primaryVlPath))
            {
                try
                {
                    var primaryDoc = XDocument.Load(primaryVlPath);
                    vlDoc.DocumentId = primaryDoc.Root?.Attribute("Id")?.Value ?? "";
                    vlDoc.LanguageVersion = primaryDoc.Root?.Attribute("LanguageVersion")?.Value ?? "";
                    vlDoc.Version = primaryDoc.Root?.Attribute("Version")?.Value ?? "";
                }
                catch { /* metadata is optional */ }
            }

            if (allVlFiles.Count == 0)
            {
                // Pure .NET package — nothing to parse from VL side
                return vlDoc;
            }

            // Merge all root-level .vl files: dependencies, patches, node definitions
            var seenDepLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var vlFile in allVlFiles)
            {
                try
                {
                    var doc = XDocument.Load(vlFile);
                    var fileName = Path.GetFileName(vlFile);

                    // Collect NuGet dependencies (de-duplicated by Location)
                    foreach (var nugetDep in doc.Root?.Elements("NugetDependency") ?? Enumerable.Empty<XElement>())
                    {
                        var location = nugetDep.Attribute("Location")?.Value ?? "";
                        if (!string.IsNullOrEmpty(location) && seenDepLocations.Add(location))
                        {
                            vlDoc.NugetDependencies.Add(new NugetDependency
                            {
                                Id = nugetDep.Attribute("Id")?.Value ?? "",
                                Location = location,
                                Version = nugetDep.Attribute("Version")?.Value ?? ""
                            });
                        }
                    }

                    // Collect all patches for complexity metrics
                    foreach (var patch in doc.Descendants("Patch"))
                    {
                        vlDoc.Patches.Add(AnalyzePatch(patch));
                    }

                    // Extract node definitions from top-level patches
                    var mainPatches = doc.Root?.Elements("Patch") ?? Enumerable.Empty<XElement>();
                    foreach (var mainPatch in mainPatches)
                    {
                        var nodeDefinitions = ExtractNodeDefinitionsFromPatch(mainPatch, fileName);
                        vlDoc.NodeDefinitions.AddRange(nodeDefinitions);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Warning: Could not parse {Path.GetFileName(vlFile)}: {ex.Message}");
                }
            }

            return vlDoc;
        }

        private string RemoveVersionSuffix(string folderName)
        {
            // Remove version patterns like ".1.0.3-alpha", ".2.1.2", etc.
            // This regex matches a dot followed by version numbers and optional pre-release identifiers
            var versionPattern = @"\.\d+(\.\d+)*(-\w+)?$";
            return System.Text.RegularExpressions.Regex.Replace(folderName, versionPattern, "");
        }


        private List<VLNodeDefinition> ExtractNodeDefinitionsFromPatch(XElement patch, string sourceDocument)
        {
            var nodeDefinitions = new List<VLNodeDefinition>();
            var canvas = patch.Element("Canvas");
            if (canvas == null) return nodeDefinitions;

            // Recursively scan the canvas hierarchy (nodes may live inside nested sub-category canvases)
            var inheritedCategory = canvas.Attribute("DefaultCategory")?.Value;
            ScanCanvas(canvas, inheritedCategory, sourceDocument, nodeDefinitions);
            return nodeDefinitions;
        }

        /// <summary>
        /// Recursively scans a Canvas element and all nested Canvas children for definition nodes.
        /// The <paramref name="inheritedCategory"/> is the closest ancestor canvas DefaultCategory.
        /// </summary>
        private void ScanCanvas(XElement canvas, string? inheritedCategory, string sourceDocument, List<VLNodeDefinition> nodeDefinitions)
        {
            // The effective category for nodes directly inside this canvas
            var canvasCategory = canvas.Attribute("DefaultCategory")?.Value;
            var effectiveCategory = !string.IsNullOrEmpty(canvasCategory) ? canvasCategory : inheritedCategory;

            // Extract definition nodes that are direct children of this canvas
            foreach (var defNode in canvas.Elements("Node").Where(IsDefinitionNode))
            {
                try
                {
                    var nodeDef = ExtractNodeDefinition(defNode, effectiveCategory, sourceDocument);
                    if (nodeDef != null)
                        nodeDefinitions.Add(nodeDef);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not extract node definition from {defNode.Attribute("Name")?.Value}: {ex.Message}");
                }
            }

            // Recurse into nested Canvas children (sub-categories)
            foreach (var nestedCanvas in canvas.Elements("Canvas"))
                ScanCanvas(nestedCanvas, effectiveCategory, sourceDocument, nodeDefinitions);
        }

        private bool IsDefinitionNode(XElement node)
        {
            var nodeRef = node.Element(XName.Get("NodeReference", "property"));
            if (nodeRef == null) return false;

            var choices = nodeRef.Elements("Choice").Select(c => c.Attribute("Kind")?.Value).ToList();
            
            // These are the kinds that represent usable node definitions
            return choices.Any(kind => 
                kind == "RecordDefinition" ||
                kind == "ClassDefinition" ||
                kind == "ContainerDefinition" ||
                kind == "OperationDefinition");
        }

        private VLNodeDefinition? ExtractNodeDefinition(XElement defNode, string? inheritedCategory, string sourceDocument)
        {
            var name = defNode.Attribute("Name")?.Value;
            if (string.IsNullOrEmpty(name)) return null;

            var nodeRef = defNode.Element(XName.Get("NodeReference", "property"));
            var choices = nodeRef?.Elements("Choice").ToList() ?? new List<XElement>();

            // Determine node type and category
            var nodeType = DetermineNodeType(choices);
            var category = DetermineCategory(nodeRef, inheritedCategory);

            var nodeDef = new VLNodeDefinition
            {
                Name = name,
                Category = category,
                Type = nodeType,
                IsGeneric = false, // Will be determined later
                Source = sourceDocument
            };

            // Extract documentation from node attributes
            nodeDef.Summary = defNode.Attribute("Summary")?.Value ?? "";
            nodeDef.Remarks = defNode.Attribute("Remarks")?.Value ?? "";
            nodeDef.Tags = ParseTags(defNode.Attribute("Tags")?.Value ?? "");

            // Find the nested patch that contains the actual definition
            var nestedPatch = defNode.Elements("Patch").FirstOrDefault();
            if (nestedPatch != null)
            {
                ExtractDefinitionDetails(nestedPatch, nodeDef);
            }

            return nodeDef;
        }

        private VLNodeType DetermineNodeType(List<XElement> choices)
        {
            foreach (var choice in choices)
            {
                var kind = choice.Attribute("Kind")?.Value;
                switch (kind)
                {
                    case "RecordDefinition":
                        return VLNodeType.Record;
                    case "ClassDefinition":
                        return VLNodeType.Class;
                    case "ContainerDefinition":
                        return VLNodeType.Process;
                    case "OperationDefinition":
                        return VLNodeType.Operation;
                }
            }
            return VLNodeType.Unknown;
        }

        private string DetermineCategory(XElement? nodeRef, string? inheritedCategory)
        {
            var fullCategoryName = nodeRef?.Attribute("LastCategoryFullName")?.Value;
            if (!string.IsNullOrEmpty(fullCategoryName))
                return fullCategoryName;

            var categoryRef = nodeRef?.Element("CategoryReference");
            var categoryName = categoryRef?.Attribute("Name")?.Value;
            if (!string.IsNullOrEmpty(categoryName) && categoryName != "Primitive")
                return categoryName;

            return !string.IsNullOrEmpty(inheritedCategory) ? inheritedCategory : "Main";
        }

        private void ExtractDefinitionDetails(XElement patch, VLNodeDefinition nodeDef)
        {
            // Check if it's generic
            nodeDef.IsGeneric = patch.Attribute("IsGeneric")?.Value == "true";

            // Extract slots (for records and classes)
            var slots = patch.Elements("Slot");
            foreach (var slot in slots)
            {
                var slotInfo = new VLSlot
                {
                    Name = slot.Attribute("Name")?.Value ?? "",
                    Summary = slot.Attribute("Summary")?.Value ?? "",
                    TypeInfo = ExtractTypeInfo(slot.Element(XName.Get("TypeAnnotation", "property")))
                };
                nodeDef.Slots.Add(slotInfo);
            }

            // Extract all named sub-patches (methods / Create / Update / etc.)
            var operationPatches = patch.Elements("Patch").Where(p => !string.IsNullOrEmpty(p.Attribute("Name")?.Value));
            foreach (var opPatch in operationPatches)
            {
                var method = ExtractMethodFromPatch(opPatch);
                if (method != null)
                    nodeDef.Methods.Add(method);
            }

            // Find ProcessDefinition to determine which methods are active
            var processDefinition = patch.Element("ProcessDefinition");
            if (processDefinition != null)
            {
                AnalyzeProcessDefinition(processDefinition, nodeDef);
            }
            else if (nodeDef.Methods.Any())
            {
                // Operations / standalone definitions — all methods are active
                nodeDef.ActiveMethods.AddRange(nodeDef.Methods);
            }
            else
            {
                // OperationDefinition whose pins live directly in the main patch
                // (no named sub-patches and no ProcessDefinition).
                // Treat the main patch itself as the single operation.
                ExtractDirectPins(patch, nodeDef);
                return; // pins already set, skip GenerateFinalPins
            }

            // Generate final pins based on slots and active methods
            GenerateFinalPins(nodeDef);
        }

        /// <summary>
        /// For Operation nodes whose <see cref="VLPin"/> elements are direct children of the
        /// main definition patch (rather than inside a named sub-patch), this method extracts
        /// them straight into <see cref="VLNodeDefinition.InputPins"/> / OutputPins.
        /// </summary>
        private void ExtractDirectPins(XElement patch, VLNodeDefinition nodeDef)
        {
            nodeDef.IsGeneric = nodeDef.IsGeneric || patch.Attribute("IsGeneric")?.Value == "true";

            foreach (var pin in patch.Elements("Pin"))
            {
                if (pin.Attribute("IsHidden")?.Value == "true") continue;

                var pinInfo = new VLPin
                {
                    Id = pin.Attribute("Id")?.Value ?? "",
                    Name = pin.Attribute("Name")?.Value ?? "",
                    Kind = pin.Attribute("Kind")?.Value ?? "",
                    DefaultValue = pin.Attribute("DefaultValue")?.Value ?? "",
                    Summary = pin.Attribute("Summary")?.Value ?? "",
                    TypeInfo = ExtractTypeInfo(pin.Element(XName.Get("TypeAnnotation", "property")))
                };

                if (pinInfo.Kind.Contains("Input"))
                    nodeDef.InputPins.Add(pinInfo);
                else if (pinInfo.Kind.Contains("Output"))
                    nodeDef.OutputPins.Add(pinInfo);
            }
        }

        private VLMethod? ExtractMethodFromPatch(XElement patch)
        {
            var name = patch.Attribute("Name")?.Value;
            if (string.IsNullOrEmpty(name)) return null;

            var method = new VLMethod
            {
                Id = patch.Attribute("Id")?.Value ?? "",
                Name = name,
                Summary = patch.Attribute("Summary")?.Value ?? "",
                Remarks = patch.Attribute("Remarks")?.Value ?? "",
                Tags = ParseTags(patch.Attribute("Tags")?.Value ?? "")
            };

            // Extract pins from the patch
            var pins = patch.Elements("Pin");
            foreach (var pin in pins)
            {
                var pinInfo = new VLPin
                {
                    Id = pin.Attribute("Id")?.Value ?? "",
                    Name = pin.Attribute("Name")?.Value ?? "",
                    Kind = pin.Attribute("Kind")?.Value ?? "",
                    IsHidden = pin.Attribute("IsHidden")?.Value == "true",
                    DefaultValue = pin.Attribute("DefaultValue")?.Value ?? "",
                    Summary = pin.Attribute("Summary")?.Value ?? "",
                    TypeInfo = ExtractTypeInfo(pin.Element(XName.Get("TypeAnnotation", "property")))
                };

                if (pinInfo.Kind.Contains("Input"))
                {
                    method.InputPins.Add(pinInfo);
                }
                else if (pinInfo.Kind.Contains("Output"))
                {
                    method.OutputPins.Add(pinInfo);
                }
            }

            return method;
        }

        private void AnalyzeProcessDefinition(XElement processDefinition, VLNodeDefinition nodeDefinition)
        {
            // Check for HasStateOut
            nodeDefinition.HasStateOut = processDefinition.Attribute("HasStateOut")?.Value == "true";

            // Get aspects
            nodeDefinition.Aspects = processDefinition.Attribute("Aspects")?.Value ?? "";

            // Find which methods are part of the process definition
            var fragments = processDefinition.Elements("Fragment");
            var activeMethodIds = new HashSet<string>();

            foreach (var fragment in fragments)
            {
                var patchId = fragment.Attribute("Patch")?.Value;
                var enabled = fragment.Attribute("Enabled")?.Value != "false"; // Default to true
                
                if (!string.IsNullOrEmpty(patchId) && enabled)
                {
                    activeMethodIds.Add(patchId);
                }
            }

            // Mark methods as active and add to ActiveMethods
            foreach (var method in nodeDefinition.Methods)
            {
                if (activeMethodIds.Contains(method.Id))
                {
                    method.IsPartOfProcessDefinition = true;
                    nodeDefinition.ActiveMethods.Add(method);
                }
            }
        }

        private void GenerateFinalPins(VLNodeDefinition nodeDef)
        {
            var inputPins = new Dictionary<string, VLPin>();
            var outputPins = new Dictionary<string, VLPin>();

            // For records and classes, slots become both setters and getters
            if (nodeDef.Type == VLNodeType.Record || nodeDef.Type == VLNodeType.Class)
            {
                foreach (var slot in nodeDef.Slots)
                {
                    // Setter (input)
                    var setterName = $"Set{slot.Name}";
                    if (nodeDef.Methods.Any(m => m.Name == setterName))
                    {
                        inputPins[slot.Name] = new VLPin
                        {
                            Name = slot.Name,
                            Kind = "InputPin",
                            Summary = slot.Summary,
                            TypeInfo = slot.TypeInfo
                        };
                    }

                    // Getter (output)
                    if (nodeDef.Methods.Any(m => m.Name == slot.Name))
                    {
                        outputPins[slot.Name] = new VLPin
                        {
                            Name = slot.Name,
                            Kind = "OutputPin",
                            Summary = slot.Summary,
                            TypeInfo = slot.TypeInfo
                        };
                    }
                }
            }

            // Add pins from active methods (for processes) or all methods (for operations)
            var relevantMethods = nodeDef.ActiveMethods.Any() ? nodeDef.ActiveMethods : nodeDef.Methods;
            
            foreach (var method in relevantMethods)
            {
                // Skip slot accessor methods as they're handled above
                if (nodeDef.Slots.Any(s => method.Name == s.Name || method.Name == $"Set{s.Name}"))
                    continue;

                foreach (var pin in method.InputPins)
                {
                    if (!pin.IsHidden)
                    {
                        inputPins[pin.Name] = pin;
                    }
                }

                foreach (var pin in method.OutputPins)
                {
                    if (!pin.IsHidden)
                    {
                        outputPins[pin.Name] = pin;
                    }
                }
            }

            nodeDef.InputPins = inputPins.Values.ToList();
            nodeDef.OutputPins = outputPins.Values.ToList();
        }

        private VLTypeInfo? ExtractTypeInfo(XElement? typeAnnotation)
{
    if (typeAnnotation == null) return null;

    var typeInfo = new VLTypeInfo
    {
        Category = typeAnnotation.Attribute("LastCategoryFullName")?.Value ?? "",
        Dependency = typeAnnotation.Attribute("LastDependency")?.Value ?? ""
    };

    // Extract choices to determine base type
    foreach (var choice in typeAnnotation.Elements("Choice"))
    {
        var choiceInfo = new VLTypeChoice
        {
            Kind = choice.Attribute("Kind")?.Value ?? "",
            Name = choice.Attribute("Name")?.Value ?? "",
            Fixed = choice.Attribute("Fixed")?.Value == "true"
        };
        typeInfo.Choices.Add(choiceInfo);
    }

    // Get the main type name from choices
    var typeChoice = typeInfo.Choices.FirstOrDefault(c => 
        c.Kind.Contains("Type") || c.Kind.Contains("Flag"));
    
    if (typeChoice != null)
    {
        var baseTypeName = typeChoice.Name;
        
        // Check for type arguments to build full generic type name
        var typeArguments = typeAnnotation.Element(XName.Get("TypeArguments", "property"));
        if (typeArguments != null)
        {
            var argumentTypes = ExtractTypeArguments(typeArguments);
            typeInfo.TypeArguments = argumentTypes; // Store for reference
            
            if (argumentTypes.Any())
            {
                typeInfo.TypeName = $"{baseTypeName}<{string.Join(",", argumentTypes)}>";
                typeInfo.IsGeneric = true;
            }
            else
            {
                typeInfo.TypeName = baseTypeName;
            }
        }
        else
        {
            typeInfo.TypeName = baseTypeName;
        }
    }

    return typeInfo;
}

private List<string> ExtractTypeArguments(XElement typeArguments)
{
    var argumentTypes = new List<string>();
    
    foreach (var typeRef in typeArguments.Elements("TypeReference"))
    {
        var argumentType = ExtractTypeFromReference(typeRef);
        if (!string.IsNullOrEmpty(argumentType))
        {
            argumentTypes.Add(argumentType);
        }
    }
    
    return argumentTypes;
}

private string ExtractTypeFromReference(XElement typeReference)
{
    // Get the base type from Choice elements
    var choice = typeReference.Elements("Choice")
        .FirstOrDefault(c => c.Attribute("Kind")?.Value?.Contains("Type") == true ||
                           c.Attribute("Kind")?.Value?.Contains("Flag") == true);
    
    if (choice == null) return "Object";
    
    var baseTypeName = choice.Attribute("Name")?.Value ?? "Object";
    
    // Check if this type reference also has type arguments (nested generics)
    var nestedTypeArguments = typeReference.Element(XName.Get("TypeArguments", "property"));
    if (nestedTypeArguments != null)
    {
        var nestedArgumentTypes = ExtractTypeArguments(nestedTypeArguments);
        if (nestedArgumentTypes.Any())
        {
            return $"{baseTypeName}<{string.Join(",", nestedArgumentTypes)}>";
        }
    }
    
    return baseTypeName;
}


        private List<string> ParseTags(string tagsString)
        {
            if (string.IsNullOrEmpty(tagsString)) return new List<string>();
            
            return tagsString.Split(',', StringSplitOptions.RemoveEmptyEntries)
                           .Select(t => t.Trim())
                           .ToList();
        }

        // Keep the original patch analysis for compatibility
        private PatchInfo AnalyzePatch(XElement patchElement)
        {
            var patch = new PatchInfo
            {
                Id = patchElement.Attribute("Id")?.Value ?? "",
                Name = patchElement.Attribute("Name")?.Value ?? "Main"
            };

            // Analyze canvas
            var canvas = patchElement.Element("Canvas");
            if (canvas != null)
            {
                patch.Canvas = new CanvasInfo
                {
                    Id = canvas.Attribute("Id")?.Value ?? "",
                    DefaultCategory = canvas.Attribute("DefaultCategory")?.Value ?? "",
                    CanvasType = canvas.Attribute("CanvasType")?.Value ?? "",
                    BordersChecked = bool.Parse(canvas.Attribute("BordersChecked")?.Value ?? "false")
                };
            }

            // Count nodes, pads, links for complexity metrics
            patch.Nodes = canvas?.Elements("Node").Select(n => new NodeInfo 
            { 
                Id = n.Attribute("Id")?.Value ?? "",
                Name = n.Attribute("Name")?.Value ?? ""
            }).ToList() ?? new List<NodeInfo>();

            patch.Pads = canvas?.Elements("Pad").Select(p => new PadInfo
            {
                Id = p.Attribute("Id")?.Value ?? "",
                Comment = p.Attribute("Comment")?.Value ?? ""
            }).ToList() ?? new List<PadInfo>();

            patch.Links = patchElement.Elements("Link").Select(l => new LinkInfo
            {
                Id = l.Attribute("Id")?.Value ?? "",
                Ids = l.Attribute("Ids")?.Value ?? ""
            }).ToList();

            return patch;
        }
    }
}

