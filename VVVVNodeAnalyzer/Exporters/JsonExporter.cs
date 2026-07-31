using System.Text.Json;
using System.Text.Json.Serialization;
using VvvvPluginAnalyzer.Models;

namespace VvvvPluginAnalyzer.Exporters
{
    public class JsonExporter
    {
        public string ExportToJson(PluginAnalysisResult result)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            return JsonSerializer.Serialize(result, options);
        }
    }
}