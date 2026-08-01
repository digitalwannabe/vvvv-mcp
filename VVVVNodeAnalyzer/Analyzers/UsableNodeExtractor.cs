using System;
using System.Collections.Generic;
using System.Linq;
using VvvvPluginAnalyzer.Models;

namespace VvvvPluginAnalyzer.Analyzers
{
    /// <summary>
    /// Converts <see cref="VLNodeDefinition"/> objects into the simplified
    /// <see cref="UsableNode"/> format consumed by the MCP catalog.
    ///
    /// Getter/Setter synthesis:
    ///   In vvvv, every Slot (field/property) of a Record or Class automatically generates
    ///   two synthesized nodes at runtime — a getter and a setter. These nodes do NOT
    ///   appear as explicit method definitions in the .vl XML; vvvv creates them from the
    ///   Slot metadata alone.  Therefore we synthesize them here from Slots directly,
    ///   without looking for SetXxx or Xxx method names.
    ///
    /// For Records (immutable):
    ///   - Getter: [RecordInstance] → [PropertyValue]
    ///   - Setter: [RecordInstance, NewValue] → [NewRecordInstance]  (returns new instance)
    ///
    /// For Classes (mutable):
    ///   - Getter: [ClassInstance] → [PropertyValue]
    ///   - Setter: [ClassInstance, NewValue] → [ClassInstance]  (same instance, modified)
    /// </summary>
    public class UsableNodeExtractor
    {
        public UsableNodesCollection ExtractUsableNodes(VLDocument document, string libraryName)
        {
            var collection = new UsableNodesCollection
            {
                LibraryName    = libraryName,
                ExtractionDate = DateTime.Now
            };

            var allNodes = ExtractNodesFromDocument(document);

            collection.Nodes = allNodes.OrderBy(n => n.Category).ThenBy(n => n.Name).ToList();
            collection.TotalNodes  = collection.Nodes.Count;
            collection.Categories  = collection.Nodes.Select(n => n.Category).Distinct().OrderBy(c => c).ToList();
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
                if (nodeDef.Name == "Application" || string.IsNullOrEmpty(nodeDef.Category))
                    continue;
                nodes.AddRange(ConvertNodeDefinitionToUsableNodes(nodeDef, document));
            }
            return nodes;
        }

        private List<UsableNode> ConvertNodeDefinitionToUsableNodes(VLNodeDefinition nodeDef, VLDocument document)
        {
            return nodeDef.Type switch
            {
                VLNodeType.Record or VLNodeType.Class => ExtractFromRecordOrClass(nodeDef, document),
                VLNodeType.Process   => [ExtractFromProcess(nodeDef, document)],
                VLNodeType.Operation => [ExtractFromOperation(nodeDef, document)],
                // Interface and Forward definitions are infrastructure, not user-facing nodes.
                // Unknown is emitted as-is for completeness.
                VLNodeType.Unknown   => [ExtractFromOperation(nodeDef, document)],
                _                    => []
            };
        }

        // ─── Record / Class ────────────────────────────────────────────────────

        private List<UsableNode> ExtractFromRecordOrClass(VLNodeDefinition nodeDef, VLDocument document)
        {
            var nodes     = new List<UsableNode>();
            var baseType  = nodeDef.Type == VLNodeType.Record ? UsableNodeType.Record : UsableNodeType.Class;
            var typeName  = nodeDef.Name;
            var category  = nodeDef.Category;
            var fullBase  = BuildFullName(category, typeName, nodeDef.Version);

            // ── 1. Constructor node ("Create") ─────────────────────────────────
            // Inputs = all slots, Output = type instance.
            var constructorNode = MakeNode(nodeDef, typeName, baseType, document, fullBase);
            foreach (var slot in nodeDef.Slots)
            {
                constructorNode.Inputs.Add(new UsablePin
                {
                    Name         = slot.Name,
                    Type         = SlotTypeName(slot),
                    Summary      = slot.Summary,
                    DefaultValue = slot.DefaultValue,
                    IsOptional   = !string.IsNullOrEmpty(slot.DefaultValue)
                });
            }
            constructorNode.Outputs.Add(new UsablePin
            {
                Name    = "Output",
                Type    = typeName,
                Summary = $"Instance of {typeName}"
            });
            nodes.Add(constructorNode);

            // ── 2. Synthesized getter nodes (one per Slot) ─────────────────────
            // Every public slot becomes a getter: [TypeInstance] → [PropertyValue]
            foreach (var slot in nodeDef.Slots)
            {
                if (string.IsNullOrEmpty(slot.Name)) continue;

                var getter = new UsableNode
                {
                    Name     = slot.Name,
                    Version  = nodeDef.Version,
                    Category = category,
                    FullName = BuildFullName(category, slot.Name, ""),
                    Type     = UsableNodeType.Getter,
                    Summary  = $"Gets {slot.Name} from {typeName}",
                    IsGeneric = nodeDef.IsGeneric,
                    HasState  = false,
                    Source    = document.FileName
                };
                getter.Inputs.Add(new UsablePin
                {
                    Name    = typeName,
                    Type    = typeName,
                    Summary = $"Input {typeName}"
                });
                getter.Outputs.Add(new UsablePin
                {
                    Name    = slot.Name,
                    Type    = SlotTypeName(slot),
                    Summary = slot.Summary
                });
                nodes.Add(getter);
            }

            // ── 3. Synthesized setter nodes (one per Slot) ─────────────────────
            // Every public slot becomes a setter: [TypeInstance, NewValue] → [TypeInstance]
            // For Records (immutable): the output is a new instance with the field replaced.
            // For Classes (mutable):  the same instance is returned after modification.
            foreach (var slot in nodeDef.Slots)
            {
                if (string.IsNullOrEmpty(slot.Name)) continue;

                var setter = new UsableNode
                {
                    Name     = $"Set {slot.Name}",
                    Version  = nodeDef.Version,
                    Category = category,
                    FullName = BuildFullName(category, $"Set {slot.Name}", ""),
                    Type     = UsableNodeType.Setter,
                    Summary  = nodeDef.Type == VLNodeType.Record
                                   ? $"Returns a new {typeName} with {slot.Name} replaced"
                                   : $"Sets {slot.Name} on the {typeName} instance",
                    IsGeneric = nodeDef.IsGeneric,
                    HasState  = false,
                    Source    = document.FileName
                };
                setter.Inputs.Add(new UsablePin
                {
                    Name    = typeName,
                    Type    = typeName,
                    Summary = $"Input {typeName}"
                });
                setter.Inputs.Add(new UsablePin
                {
                    Name         = slot.Name,
                    Type         = SlotTypeName(slot),
                    Summary      = slot.Summary,
                    DefaultValue = slot.DefaultValue,
                    IsOptional   = !string.IsNullOrEmpty(slot.DefaultValue)
                });
                setter.Outputs.Add(new UsablePin
                {
                    Name    = "Output",
                    Type    = typeName,
                    Summary = nodeDef.Type == VLNodeType.Record
                                  ? $"New {typeName} with {slot.Name} set"
                                  : $"Modified {typeName}"
                });
                nodes.Add(setter);
            }

            // ── 4. Explicit operation methods (Update, Dispose, custom) ────────
            // Skip Create (already the constructor), and any slot accessor names
            // that might have been manually defined (unusual but possible).
            var slotNames = new HashSet<string>(nodeDef.Slots.Select(s => s.Name), StringComparer.Ordinal);
            foreach (var method in nodeDef.Methods)
            {
                if (method.Name == "Create") continue;
                if (slotNames.Contains(method.Name)) continue;
                if (method.Name.StartsWith("Set ") &&
                    slotNames.Contains(method.Name[4..])) continue;

                var methodNode = MakeNode(nodeDef, method.Name, UsableNodeType.Method, document,
                    BuildFullName(category, $"{typeName}.{method.Name}", ""));
                methodNode.Summary  = method.Summary;
                methodNode.Remarks  = method.Remarks;
                methodNode.Tags     = method.Tags;

                foreach (var pin in method.InputPins.Where(p => !p.IsHidden))
                    methodNode.Inputs.Add(ConvertPin(pin));
                foreach (var pin in method.OutputPins.Where(p => !p.IsHidden))
                    methodNode.Outputs.Add(ConvertPin(pin));

                nodes.Add(methodNode);
            }

            return nodes;
        }

        // ─── Process ───────────────────────────────────────────────────────────

        private UsableNode ExtractFromProcess(VLNodeDefinition nodeDef, VLDocument document)
        {
            var node = MakeNode(nodeDef, nodeDef.Name, UsableNodeType.Process, document,
                BuildFullName(nodeDef.Category, nodeDef.Name, nodeDef.Version));
            node.HasState = true; // all process nodes are stateful by definition

            // Prefer the Update operation pins (the primary user-facing interface).
            // Fall back to all active methods if no Update is present.
            var updateMethod = nodeDef.ActiveMethods.FirstOrDefault(m =>
                m.Name.Equals("Update", StringComparison.OrdinalIgnoreCase));
            var sources = updateMethod != null
                ? [updateMethod]
                : (nodeDef.ActiveMethods.Count > 0 ? nodeDef.ActiveMethods : nodeDef.Methods);

            var seenIn  = new HashSet<string>(StringComparer.Ordinal);
            var seenOut = new HashSet<string>(StringComparer.Ordinal);
            foreach (var method in sources)
            {
                foreach (var pin in method.InputPins.Where(p => !p.IsHidden))
                    if (seenIn.Add(pin.Name)) node.Inputs.Add(ConvertPin(pin));
                foreach (var pin in method.OutputPins.Where(p => !p.IsHidden))
                    if (seenOut.Add(pin.Name)) node.Outputs.Add(ConvertPin(pin));
            }

            // Fall back to pre-computed pins if methods produced nothing
            if (node.Inputs.Count == 0 && node.Outputs.Count == 0)
            {
                foreach (var pin in nodeDef.InputPins)  node.Inputs.Add(ConvertPin(pin));
                foreach (var pin in nodeDef.OutputPins) node.Outputs.Add(ConvertPin(pin));
            }

            return node;
        }

        // ─── Operation ─────────────────────────────────────────────────────────

        private UsableNode ExtractFromOperation(VLNodeDefinition nodeDef, VLDocument document)
        {
            var node = MakeNode(nodeDef, nodeDef.Name, UsableNodeType.Operation, document,
                BuildFullName(nodeDef.Category, nodeDef.Name, nodeDef.Version));

            var mainMethod = nodeDef.Methods.FirstOrDefault();
            if (mainMethod != null)
            {
                foreach (var pin in mainMethod.InputPins.Where(p => !p.IsHidden))
                    node.Inputs.Add(ConvertPin(pin));
                foreach (var pin in mainMethod.OutputPins.Where(p => !p.IsHidden))
                    node.Outputs.Add(ConvertPin(pin));
            }
            else
            {
                foreach (var pin in nodeDef.InputPins)  node.Inputs.Add(ConvertPin(pin));
                foreach (var pin in nodeDef.OutputPins) node.Outputs.Add(ConvertPin(pin));
            }

            return node;
        }

        // ─── Helpers ───────────────────────────────────────────────────────────

        private static UsableNode MakeNode(VLNodeDefinition nodeDef, string name,
            UsableNodeType type, VLDocument document, string fullName)
        {
            return new UsableNode
            {
                Name     = name,
                Version  = nodeDef.Version,
                Category = nodeDef.Category,
                FullName = fullName,
                Type     = type,
                Summary  = nodeDef.Summary,
                Remarks  = nodeDef.Remarks,
                Tags     = nodeDef.Tags,
                IsGeneric = nodeDef.IsGeneric,
                HasState  = nodeDef.HasStateOut,
                Source    = document.FileName
            };
        }

        /// <summary>
        /// Builds the full name: "Category.Name" or "Category.Name (Version)" when version is set.
        /// </summary>
        private static string BuildFullName(string category, string name, string version)
        {
            var base_ = string.IsNullOrEmpty(category) ? name : $"{category}.{name}";
            return string.IsNullOrEmpty(version) ? base_ : $"{base_} ({version})";
        }

        private static string SlotTypeName(VLSlot slot) =>
            slot.TypeInfo?.TypeName is { Length: > 0 } t ? t : "Object";

        private static UsablePin ConvertPin(VLPin vlPin)
        {
            return new UsablePin
            {
                Name         = vlPin.Name,
                Type         = vlPin.TypeInfo?.TypeName is { Length: > 0 } t ? t : "Object",
                Summary      = vlPin.Summary,
                DefaultValue = vlPin.DefaultValue,
                // A pin is optional if it has an explicit Optional/OnlyInspector visibility,
                // or if it has a default value (heuristic for non-annotated pins).
                IsOptional   = vlPin.IsOptional || !string.IsNullOrEmpty(vlPin.DefaultValue),
                IsGeneric    = vlPin.TypeInfo?.IsGeneric ?? false
            };
        }
    }
}
