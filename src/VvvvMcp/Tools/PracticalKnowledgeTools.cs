using System.ComponentModel;
using ModelContextProtocol.Server;
using VvvvMcp.Core.Models;
using VvvvMcp.Core.Services;

namespace VvvvMcp.Tools;

/// <summary>
/// MCP tools for searching the practical vvvv knowledge index.
///
/// "Practical" knowledge is data that doesn't fit neatly into the curated
/// knowledge docs or the node catalog — help patch examples showing nodes
/// in context, forum solutions, dev-team answers, and code snippets.
///
/// Requires the relevant sources to be indexed first:
///   - Help patches: ./scripts/index-help-patches.ps1
///   - Forum data:   ./scripts/scrape-forum.ps1
/// The MCP server ingests these JSON/MD files into SQLite at startup.
/// </summary>
[McpServerToolType]
public class PracticalKnowledgeTools
{
    private readonly SearchIndexService _index;

    public PracticalKnowledgeTools(SearchIndexService index)
    {
        _index = index;
    }

    [McpServerTool(Name = "search_practical")]
    [Description("""
        Search the practical vvvv knowledge index: help patch examples, forum solutions,
        dev-team answers, and code snippets. Uses SQLite FTS5 BM25 ranking.

        Source types:
          help-patch     – which help files demonstrate a given node or pattern
          forum-solution – Discourse accepted solutions on forum.vvvv.org
          forum-dev      – high-score responses from vvvv core team / power users
          forum-snippet  – code blocks extracted from forum posts (C#, SDSL, VL XML)

        Build the practical index first:
          ./scripts/index-help-patches.ps1   (help patches)
          ./scripts/scrape-forum.ps1         (forum data)

        Examples:
          search_practical("ForEach region spread")
          search_practical("TextureFX FilterBase")
          search_practical("dynamic enum ProcessNode", source="forum-solution")
          search_practical("IChannelHub publish", source="forum-dev")
        """)]
    public object SearchPractical(
        [Description("Search query — natural language or keywords (e.g. 'ForEach spread iteration', 'TextureFX filter', 'IObservable reactive')")] string query,
        [Description("Optional source filter: 'help-patch', 'forum-solution', 'forum-dev', 'forum-snippet'. Omit to search all.")] string? source = null,
        [Description("Maximum results to return (default 10, max 30)")] int limit = 10)
    {
        if (!_index.IsReady)
            return new
            {
                error = "Practical knowledge index not ready.",
                hint  = "Run ./scripts/index-help-patches.ps1 and/or ./scripts/scrape-forum.ps1, then restart the MCP server."
            };

        limit = Math.Clamp(limit, 1, 30);
        var results = _index.SearchPractical(query, source, limit);

        if (results.Count == 0)
        {
            var (_, _, practicalCount) = _index.GetStats();
            return new
            {
                query,
                source,
                count          = 0,
                practicalTotal = practicalCount,
                message        = practicalCount == 0
                    ? "No practical knowledge indexed yet. Run index-help-patches.ps1 and/or scrape-forum.ps1."
                    : $"No results for '{query}'. Try broader terms."
            };
        }

        return new
        {
            query,
            source,
            count   = results.Count,
            results = results.Select(r => new
            {
                source   = r.Source,
                title    = r.Title,
                snippet  = r.Snippet,
                code     = string.IsNullOrWhiteSpace(r.CodeSnippet) ? null : r.CodeSnippet,
                url      = r.Url,
                author   = r.Author,
                score    = Math.Round(r.Score, 3)
            }).ToArray()
        };
    }

    [McpServerTool(Name = "get_index_stats")]
    [Description("""
        Returns the current size of the SQLite search index across all three tables.
        Use this to verify that knowledge, nodes, and practical data are indexed.

        Returns:
          knowledge  – number of .md knowledge files indexed
          nodes      – number of vvvv nodes indexed (should be ~6,400)
          practical  – number of practical entries (help patches, forum posts, etc.)
        """)]
    public object GetIndexStats()
    {
        if (!_index.IsReady)
            return new { error = "Search index not initialized." };

        var (knowledge, nodes, practical) = _index.GetStats();
        return new
        {
            knowledge,
            nodes,
            practical,
            status = _index.IsReady ? "ready" : "not ready",
            hint   = practical == 0
                ? "Run ./scripts/index-help-patches.ps1 and/or ./scripts/scrape-forum.ps1 to populate practical index."
                : null
        };
    }
}
