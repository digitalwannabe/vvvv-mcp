using System.ComponentModel;
using ModelContextProtocol.Server;
using VvvvMcp.Core.Services;

namespace VvvvMcp.Tools;

[McpServerToolType]
public class KnowledgeTools
{
    private readonly KnowledgeService _knowledge;

    public KnowledgeTools(KnowledgeService knowledge)
    {
        _knowledge = knowledge;
    }

    [McpServerTool(Name = "list_knowledge")]
    [Description("List all available vvvv knowledge documents. Returns names and descriptions of all knowledge files covering vvvv concepts, file format, patching patterns, packages, custom nodes, etc.")]
    public object ListKnowledge()
    {
        if (!_knowledge.IsLoaded)
            return new { error = "Knowledge base not loaded.", note = "Set VVVV_MCP_KNOWLEDGE environment variable." };

        var files = _knowledge.ListFiles();
        return new
        {
            count = files.Count,
            documents = files.Select(f => new { f.Name, f.Description })
        };
    }

    [McpServerTool(Name = "read_knowledge")]
    [Description("Read the full content of a vvvv knowledge document by name. Use list_knowledge first to see available documents.")]
    public object ReadKnowledge(
        [Description("Document name (e.g. 'vvvv-concepts', 'vl-file-format', 'vvvv-patching', 'vvvv-custom-nodes', 'vvvv-packages')")] string name)
    {
        if (!_knowledge.IsLoaded)
            return new { error = "Knowledge base not loaded." };

        var file = _knowledge.GetFile(name);
        if (file is null)
        {
            var available = _knowledge.ListFiles().Select(f => f.Name).ToList();
            return new
            {
                error = $"Knowledge document '{name}' not found.",
                available
            };
        }

        return new
        {
            name = file.Name,
            description = file.Description,
            content = file.Content
        };
    }

    [McpServerTool(Name = "search_knowledge")]
    [Description("Search across all vvvv knowledge documents for a specific topic. Returns relevant snippets from matching documents. Useful for finding information about specific vvvv concepts, nodes, patterns, or packages.")]
    public object SearchKnowledge(
        [Description("Search query (e.g. 'Spread iteration', 'TransformSRT', 'Stride RootScene', 'IOBox Float32')")] string query,
        [Description("Maximum number of results (default 3)")] int limit = 3)
    {
        if (!_knowledge.IsLoaded)
            return new { error = "Knowledge base not loaded." };

        if (string.IsNullOrWhiteSpace(query))
            return new { error = "Query cannot be empty." };

        limit = Math.Clamp(limit, 1, 10);
        var results = _knowledge.Search(query, limit);

        if (!results.Any())
            return new { query, results = Array.Empty<object>(), hint = "Try broader search terms or use list_knowledge to see available documents." };

        return new
        {
            query,
            resultCount = results.Count,
            results = results.Select(r => new
            {
                r.Name,
                r.Description,
                r.Snippet,
                score = Math.Round(r.Score, 1)
            })
        };
    }
}
