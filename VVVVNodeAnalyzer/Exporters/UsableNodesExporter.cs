using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VvvvPluginAnalyzer.Models;

namespace VvvvPluginAnalyzer.Exporters
{
    public class UsableNodesExporter
    {
        private readonly JsonSerializerOptions _jsonOptions;

        public UsableNodesExporter()
        {
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Converters = { new JsonStringEnumConverter() }
            };
        }

        public string ExportToJson(UsableNodesCollection collection)
        {
            return JsonSerializer.Serialize(collection, _jsonOptions);
        }

        public void ExportToFile(UsableNodesCollection collection, string filePath)
        {
            var json = ExportToJson(collection);
            File.WriteAllText(filePath, json);
        }

        public void ExportToMarkdown(UsableNodesCollection collection, string filePath)
        {
            using var writer = new StreamWriter(filePath);
            
            writer.WriteLine($"# {collection.LibraryName} - Usable Nodes");
            writer.WriteLine();
            
            if (!string.IsNullOrEmpty(collection.Description))
            {
                writer.WriteLine(collection.Description);
                writer.WriteLine();
            }

            writer.WriteLine("## Summary");
            writer.WriteLine($"- **Total Nodes**: {collection.TotalNodes}");
            writer.WriteLine($"- **Categories**: {collection.Categories.Count}");
            writer.WriteLine();

            if (collection.NodesByType.Count > 0)
            {
                writer.WriteLine("### Nodes by Type");
                foreach (var kvp in collection.NodesByType)
                {
                    writer.WriteLine($"- **{kvp.Key}**: {kvp.Value}");
                }
                writer.WriteLine();
            }

            writer.WriteLine("### Categories");
            foreach (var category in collection.Categories)
            {
                var nodesInCategory = collection.Nodes.Count(n => n.Category == category);
                writer.WriteLine($"- **{category}**: {nodesInCategory} nodes");
            }
            writer.WriteLine();

            writer.WriteLine("## Nodes");
            writer.WriteLine();

            string currentCategory = "";
            foreach (var node in collection.Nodes)
            {
                if (node.Category != currentCategory)
                {
                    currentCategory = node.Category;
                    writer.WriteLine($"### {currentCategory}");
                    writer.WriteLine();
                }

                writer.WriteLine($"#### {node.Name}");
                writer.WriteLine();
                
                if (!string.IsNullOrEmpty(node.Summary))
                {
                    writer.WriteLine($"**Summary**: {node.Summary}");
                    writer.WriteLine();
                }

                if (!string.IsNullOrEmpty(node.Remarks))
                {
                    writer.WriteLine($"**Remarks**: {node.Remarks}");
                    writer.WriteLine();
                }

                if (node.Tags.Count > 0)
                {
                    writer.WriteLine($"**Tags**: {string.Join(", ", node.Tags)}");
                    writer.WriteLine();
                }

                writer.WriteLine($"**Type**: {node.Type}");
                if (node.IsGeneric) writer.WriteLine("**Generic**: Yes");
                if (node.HasState) writer.WriteLine("**Has State**: Yes");
                writer.WriteLine();

                if (node.Inputs.Count > 0)
                {
                    writer.WriteLine("**Inputs**:");
                    foreach (var input in node.Inputs)
                    {
                        var optional = input.IsOptional ? " (optional)" : "";
                        var defaultVal = !string.IsNullOrEmpty(input.DefaultValue) ? $" = {input.DefaultValue}" : "";
                        writer.WriteLine($"- `{input.Name}` ({input.Type}){defaultVal}{optional}");
                        if (!string.IsNullOrEmpty(input.Summary))
                        {
                            writer.WriteLine($"  - {input.Summary}");
                        }
                    }
                    writer.WriteLine();
                }

                if (node.Outputs.Count > 0)
                {
                    writer.WriteLine("**Outputs**:");
                    foreach (var output in node.Outputs)
                    {
                        writer.WriteLine($"- `{output.Name}` ({output.Type})");
                        if (!string.IsNullOrEmpty(output.Summary))
                        {
                            writer.WriteLine($"  - {output.Summary}");
                        }
                    }
                    writer.WriteLine();
                }

                writer.WriteLine("---");
                writer.WriteLine();
            }
        }
    }
}