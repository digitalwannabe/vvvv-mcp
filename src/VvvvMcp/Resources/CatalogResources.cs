using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using VvvvMcp.Core.Services;

namespace VvvvMcp.Resources;

[McpServerResourceType]
public class CatalogResources
{
    private readonly NodeCatalogService _catalog;

    public CatalogResources(NodeCatalogService catalog)
    {
        _catalog = catalog;
    }

    [McpServerResource(Name = "Catalog Statistics", UriTemplate = "vvvv://catalog/stats")]
    [Description("Get statistics about the loaded vvvv node catalog: total nodes, packages, categories.")]
    public string GetCatalogStats()
    {
        if (!_catalog.IsLoaded)
            return "Node catalog not loaded.";

        var stats = _catalog.GetStats();
        return JsonSerializer.Serialize(stats, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });
    }

    [McpServerResource(Name = "Category List", UriTemplate = "vvvv://catalog/categories")]
    [Description("List all node categories in the vvvv catalog.")]
    public string GetCategories()
    {
        if (!_catalog.IsLoaded)
            return "Node catalog not loaded.";

        var categories = _catalog.GetCategories();
        return JsonSerializer.Serialize(categories, new JsonSerializerOptions { WriteIndented = true });
    }
}
