using System;
using System.Collections.Generic;
using System.Linq;
using VvvvPluginAnalyzer.Models;

namespace VvvvPluginAnalyzer.Analyzers
{
    public class UsableNodeExtractor
    {
        public UsableNodesCollection ExtractUsableNodes(VLDocument document, string libraryName)
        {
            var collection = new UsableNodesCollection
            {
                LibraryName = libraryName,
                ExtractionDate = DateTime.Now
            };

            var allNodes = new List<UsableNode>();


                var documentNodes = ExtractNodesFromDocument(document);
                allNodes.AddRange(documentNodes);
            

            // Sort nodes by category and name
            collection.Nodes = allNodes.OrderBy(n => n.Category).ThenBy(n => n.Name).ToList();
            
            // Calculate summary statistics
            collection.TotalNodes = collection.Nodes.Count;
            collection.Categories = collection.Nodes.Select(n => n.Category).Distinct().OrderBy(c => c).ToList();
            
            // Group by node type
            collection.NodesByType = collection.Nodes
                .GroupBy(n => n.Type.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            return collection;
        }

        private List<UsableNode> ExtractNodesFromDocument(VLDocument document)
        {
            var nodes = new List<UsableNode>();

            foreach (var nodeDef in document.NodeDefinitions)
            {
                // Skip Application nodes and other non-library nodes
                if (nodeDef.Name == "Application" || string.IsNullOrEmpty(nodeDef.Category))
                    continue;

                var usableNodes = ConvertNodeDefinitionToUsableNodes(nodeDef, document);
                nodes.AddRange(usableNodes);
            }

            return nodes;
        }

        private List<UsableNode> ConvertNodeDefinitionToUsableNodes(VLNodeDefinition nodeDef, VLDocument document)
        {
            var nodes = new List<UsableNode>();

            switch (nodeDef.Type)
            {
                case VLNodeType.Record:
                case VLNodeType.Class:
                    nodes.AddRange(ExtractFromRecordOrClass(nodeDef, document));
                    break;

                case VLNodeType.Process:
                    nodes.Add(ExtractFromProcess(nodeDef, document));
                    break;

                case VLNodeType.Operation:
                    nodes.Add(ExtractFromOperation(nodeDef, document));
                    break;
            }

            return nodes;
        }

        private List<UsableNode> ExtractFromRecordOrClass(VLNodeDefinition nodeDef, VLDocument document)
        {
            var nodes = new List<UsableNode>();

            // 1. Constructor node (Create operation)
            var createMethod = nodeDef.Methods.FirstOrDefault(m => m.Name == "Create");
            if (createMethod != null)
            {
                var constructorNode = new UsableNode
                {
                    Name = nodeDef.Name,
                    FullName = $"{nodeDef.Category}.{nodeDef.Name}",
                    Category = nodeDef.Category,
                    Type = nodeDef.Type == VLNodeType.Record ? UsableNodeType.Record : UsableNodeType.Class,
                    Summary = nodeDef.Summary,
                    Remarks = nodeDef.Remarks,
                    Tags = nodeDef.Tags,
                    IsGeneric = nodeDef.IsGeneric,
                    HasState = nodeDef.HasStateOut,
                    Source = document.FileName
                };

                // Constructor inputs from slots
                foreach (var slot in nodeDef.Slots)
                {
                    constructorNode.Inputs.Add(new UsablePin
                    {
                        Name = slot.Name,
                        Type = slot.TypeInfo?.TypeName ?? "Object",
                        Summary = slot.Summary,
                        IsOptional = false
                    });
                }

                // Constructor output is the instance
                constructorNode.Outputs.Add(new UsablePin
                {
                    Name = "Output",
                    Type = nodeDef.Name,
                    Summary = $"Instance of {nodeDef.Name}"
                });

                nodes.Add(constructorNode);
            }

            // 2. Property setter nodes
            foreach (var slot in nodeDef.Slots)
            {
                var setterMethod = nodeDef.Methods.FirstOrDefault(m => m.Name == $"Set{slot.Name}");
                if (setterMethod != null)
                {
                    var setterNode = new UsableNode
                    {
                        Name = $"Set{slot.Name}",
                        FullName = $"{nodeDef.Category}.{nodeDef.Name}.Set{slot.Name}",
                        Category = nodeDef.Category,
                        Type = UsableNodeType.Setter,
                        Summary = $"Sets the {slot.Name} property",
                        IsGeneric = nodeDef.IsGeneric,
                        Source = document.FileName
                    };

                    // Instance input
                    setterNode.Inputs.Add(new UsablePin
                    {
                        Name = "Input",
                        Type = nodeDef.Name,
                        Summary = $"Instance of {nodeDef.Name}"
                    });

                    // Value input
                    setterNode.Inputs.Add(new UsablePin
                    {
                        Name = slot.Name,
                        Type = slot.TypeInfo?.TypeName ?? "Object",
                        Summary = slot.Summary
                    });

                    // Output is the modified instance
                    setterNode.Outputs.Add(new UsablePin
                    {
                        Name = "Output",
                        Type = nodeDef.Name,
                        Summary = $"Modified instance of {nodeDef.Name}"
                    });

                    nodes.Add(setterNode);
                }
            }

            // 3. Property getter nodes
            foreach (var slot in nodeDef.Slots)
            {
                var getterMethod = nodeDef.Methods.FirstOrDefault(m => m.Name == slot.Name);
                if (getterMethod != null)
                {
                    var getterNode = new UsableNode
                    {
                        Name = slot.Name,
                        FullName = $"{nodeDef.Category}.{nodeDef.Name}.{slot.Name}",
                        Category = nodeDef.Category,
                        Type = UsableNodeType.Getter,
                        Summary = $"Gets the {slot.Name} property",
                        IsGeneric = nodeDef.IsGeneric,
                        Source = document.FileName
                    };

                    // Instance input
                    getterNode.Inputs.Add(new UsablePin
                    {
                        Name = "Input",
                        Type = nodeDef.Name,
                        Summary = $"Instance of {nodeDef.Name}"
                    });

                    // Property output
                    getterNode.Outputs.Add(new UsablePin
                    {
                        Name = slot.Name,
                        Type = slot.TypeInfo?.TypeName ?? "Object",
                        Summary = slot.Summary
                    });

                    nodes.Add(getterNode);
                }
            }

            // 4. Additional methods (if any)
            var additionalMethods = nodeDef.Methods.Where(m => 
                m.Name != "Create" && 
                !nodeDef.Slots.Any(s => m.Name == s.Name || m.Name == $"Set{s.Name}"));

            foreach (var method in additionalMethods)
            {
                var methodNode = new UsableNode
                {
                    Name = method.Name,
                    FullName = $"{nodeDef.Category}.{nodeDef.Name}.{method.Name}",
                    Category = nodeDef.Category,
                    Type = UsableNodeType.Method,
                    Summary = method.Summary,
                    Remarks = method.Remarks,
                    Tags = method.Tags,
                    IsGeneric = nodeDef.IsGeneric,
                    Source = document.FileName
                };

                // Convert method pins
                foreach (var pin in method.InputPins)
                {
                    methodNode.Inputs.Add(ConvertVLPinToUsablePin(pin));
                }

                foreach (var pin in method.OutputPins)
                {
                    methodNode.Outputs.Add(ConvertVLPinToUsablePin(pin));
                }

                nodes.Add(methodNode);
            }

            return nodes;
        }

        private UsableNode ExtractFromProcess(VLNodeDefinition nodeDef, VLDocument document)
        {
            var processNode = new UsableNode
            {
                Name = nodeDef.Name,
                FullName = $"{nodeDef.Category}.{nodeDef.Name}",
                Category = nodeDef.Category,
                Type = UsableNodeType.Process,
                Summary = nodeDef.Summary,
                Remarks = nodeDef.Remarks,
                Tags = nodeDef.Tags,
                IsGeneric = nodeDef.IsGeneric,
                HasState = nodeDef.HasStateOut,
                Source = document.FileName
            };

            // For processes, we combine pins from all active methods
            var allInputPins = new Dictionary<string, UsablePin>();
            var allOutputPins = new Dictionary<string, UsablePin>();

            foreach (var method in nodeDef.ActiveMethods)
            {
                foreach (var pin in method.InputPins.Where(p => !p.IsHidden))
                {
                    var usablePin = ConvertVLPinToUsablePin(pin);
                    allInputPins[pin.Name] = usablePin;
                }

                foreach (var pin in method.OutputPins.Where(p => !p.IsHidden))
                {
                    var usablePin = ConvertVLPinToUsablePin(pin);
                    allOutputPins[pin.Name] = usablePin;
                }
            }

            processNode.Inputs = allInputPins.Values.ToList();
            processNode.Outputs = allOutputPins.Values.ToList();

            return processNode;
        }

        private UsableNode ExtractFromOperation(VLNodeDefinition nodeDef, VLDocument document)
        {
            var operationNode = new UsableNode
            {
                Name = nodeDef.Name,
                FullName = $"{nodeDef.Category}.{nodeDef.Name}",
                Category = nodeDef.Category,
                Type = UsableNodeType.Operation,
                Summary = nodeDef.Summary,
                Remarks = nodeDef.Remarks,
                Tags = nodeDef.Tags,
                IsGeneric = nodeDef.IsGeneric,
                Source = document.FileName
            };

            // For operations, we use the pins from the main operation method
            var mainMethod = nodeDef.Methods.FirstOrDefault();
            if (mainMethod != null)
            {
                foreach (var pin in mainMethod.InputPins.Where(p => !p.IsHidden))
                {
                    operationNode.Inputs.Add(ConvertVLPinToUsablePin(pin));
                }

                foreach (var pin in mainMethod.OutputPins.Where(p => !p.IsHidden))
                {
                    operationNode.Outputs.Add(ConvertVLPinToUsablePin(pin));
                }
            }
            else
            {
                // Fallback to the computed pins from the node definition
                foreach (var pin in nodeDef.InputPins)
                {
                    operationNode.Inputs.Add(ConvertVLPinToUsablePin(pin));
                }

                foreach (var pin in nodeDef.OutputPins)
                {
                    operationNode.Outputs.Add(ConvertVLPinToUsablePin(pin));
                }
            }

            return operationNode;
        }

        private UsablePin ConvertVLPinToUsablePin(VLPin vlPin)
        {
            return new UsablePin
            {
                Name = vlPin.Name,
                Type = vlPin.TypeInfo?.TypeName ?? "Object",
                Summary = vlPin.Summary,
                DefaultValue = vlPin.DefaultValue,
                IsOptional = !string.IsNullOrEmpty(vlPin.DefaultValue)
            };
        }
    }
}