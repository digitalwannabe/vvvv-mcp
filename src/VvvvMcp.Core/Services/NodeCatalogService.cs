using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using VvvvMcp.Core.Models;
using Microsoft.Extensions.Logging;

namespace VvvvMcp.Core.Services;

public class NodeCatalogService
{
    private readonly ILogger<NodeCatalogService> _logger;
    private NodeCatalog? _catalog;
    private SearchIndexService? _searchIndex;
    private readonly Dictionary<string, List<VvvvNode>> _categoryIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<VvvvNode>> _packageIndex = new(StringComparer.OrdinalIgnoreCase);
    // Key: FullName (category.name), value: list because different packages may share a full name
    private readonly Dictionary<string, List<VvvvNode>> _fullNameIndex = new(StringComparer.OrdinalIgnoreCase);

    public NodeCatalogService(ILogger<NodeCatalogService> logger)
    {
        _logger = logger;
    }

    /// <summary>Wire in the FTS5 search index after it has been initialized.</summary>
    public void SetSearchIndex(SearchIndexService index) => _searchIndex = index;

    public async Task LoadAsync(string catalogPath, CancellationToken ct = default)
    {
        _logger.LogInformation("Loading node catalog from {Path}", catalogPath);

        await using var stream = File.OpenRead(catalogPath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) }
        };

        _catalog = await JsonSerializer.DeserializeAsync<NodeCatalog>(stream, options, ct)
            ?? throw new InvalidOperationException("Failed to deserialize node catalog");

        BuildIndices();

        var packageCount = _packageIndex.Count;
        _logger.LogInformation("Loaded {NodeCount} nodes from {PackageCount} packages, {CategoryCount} categories",
            _catalog.Nodes.Count, packageCount, _categoryIndex.Count);
    }

    private void BuildIndices()
    {
        if (_catalog is null) return;

        _categoryIndex.Clear();
        _packageIndex.Clear();
        _fullNameIndex.Clear();

        foreach (var node in _catalog.Nodes)
        {
            // Full-name index (Category.Name)
            if (!string.IsNullOrWhiteSpace(node.FullName))
            {
                if (!_fullNameIndex.TryGetValue(node.FullName, out var fnList))
                {
                    fnList = new List<VvvvNode>();
                    _fullNameIndex[node.FullName] = fnList;
                }
                fnList.Add(node);
            }

            // Category index
            if (!TryNormalizeCategory(node.Category, out var normalizedCategory))
                continue;

            if (!_categoryIndex.TryGetValue(normalizedCategory, out var catList))
            {
                catList = new List<VvvvNode>();
                _categoryIndex[normalizedCategory] = catList;
            }
            catList.Add(node);

            // Package index — derived from Source field
            var pkg = node.Source;
            if (!string.IsNullOrWhiteSpace(pkg))
            {
                if (!_packageIndex.TryGetValue(pkg, out var pkgList))
                {
                    pkgList = new List<VvvvNode>();
                    _packageIndex[pkg] = pkgList;
                }
                pkgList.Add(node);
            }
        }
    }

    public bool IsLoaded => _catalog is not null;

    /// <summary>All nodes in the catalog — used by SearchIndexService for bulk indexing.</summary>
    public IReadOnlyList<VvvvNode> GetAllNodes() => _catalog?.Nodes ?? [];

    public List<NodeSearchResult> Search(string query, string? category = null, int limit = 25)
    {
        EnsureLoaded();

        // Use FTS5 when available — returns (FullName, Package, Score) hits
        if (_searchIndex?.IsReady == true)
        {
            var hits    = _searchIndex.SearchNodeHits(query, category, limit);
            var results = new List<NodeSearchResult>(hits.Count);
            foreach (var (fullName, pkg, score) in hits)
            {
                // Look up the full node object from the in-memory index
                if (_fullNameIndex.TryGetValue(fullName, out var candidates))
                {
                    var node = candidates.Count == 1
                        ? candidates[0]
                        : candidates.FirstOrDefault(n =>
                            n.Package.Equals(pkg, StringComparison.OrdinalIgnoreCase))
                          ?? candidates[0];
                    results.Add(new NodeSearchResult(node, score));
                }
            }
            return results.OrderByDescending(r => r.Score).ToList();
        }

        // Fallback: in-memory relevance scoring
        return SearchInMemory(query, category, limit);
    }

    private List<NodeSearchResult> SearchInMemory(string query, string? category, int limit)
    {
        var queryLower = query.ToLowerInvariant();
        var queryTerms = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var results    = new List<NodeSearchResult>();

        IEnumerable<VvvvNode> searchSpace = _catalog!.Nodes;

        if (!string.IsNullOrEmpty(category))
        {
            searchSpace = searchSpace.Where(n =>
                TryNormalizeCategory(n.Category, out var nc) &&
                nc.Contains(category, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var node in searchSpace)
        {
            double score = CalculateRelevance(node, queryLower, queryTerms);
            if (score > 0)
                results.Add(new NodeSearchResult(node, score));
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(limit)
            .ToList();
    }

    private static double CalculateRelevance(VvvvNode node, string queryLower, string[] queryTerms)
    {
        double score = 0;
        var nameLower = node.Name.ToLowerInvariant();
        var categoryLower = node.Category.ToLowerInvariant();
        var summaryLower = node.Summary.ToLowerInvariant();
        var fullNameLower = node.FullName.ToLowerInvariant();

        if (nameLower == queryLower) score += 100;
        else if (nameLower.StartsWith(queryLower)) score += 50;
        else if (nameLower.Contains(queryLower)) score += 25;

        if (fullNameLower.Contains(queryLower)) score += 20;
        if (categoryLower.Contains(queryLower)) score += 15;
        if (summaryLower.Contains(queryLower)) score += 10;

        // Also match tags
        if (node.Tags.Any(t => t.Contains(queryLower, StringComparison.OrdinalIgnoreCase)))
            score += 8;

        if (queryTerms.Length > 1)
        {
            int matchedTerms = queryTerms.Count(t =>
                nameLower.Contains(t) || categoryLower.Contains(t) || summaryLower.Contains(t));
            score += matchedTerms * 5;
        }

        if (!string.IsNullOrEmpty(node.Summary)) score *= 1.2;
        if (node.Category == "Unknown") score *= 0.8;

        return score;
    }

    /// <summary>Find nodes by exact name (case-insensitive).</summary>
    public List<VvvvNode> GetByName(string name)
    {
        EnsureLoaded();
        return _catalog!.Nodes
            .Where(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Tolerant lookup for user/agent-supplied names:
    /// exact name → exact full name ("Stride.Models.Box") → full name with variant
    /// ("Rotation (Successive)") → full name suffix (".Box") → name contains.
    /// </summary>
    public List<VvvvNode> FindTolerant(string name)
    {
        EnsureLoaded();

        var exact = GetByName(name);
        if (exact.Count > 0) return exact;

        if (_fullNameIndex.TryGetValue(name, out var byFullName))
            return byFullName;

        // "Rotation (Successive)" style: FullName ends with the variant form
        var variant = _catalog!.Nodes
            .Where(n => n.FullName.EndsWith($".{name}", StringComparison.OrdinalIgnoreCase)
                     || n.FullName.Equals(name, StringComparison.OrdinalIgnoreCase)
                     || n.FullName.EndsWith($" ({name})", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (variant.Count > 0) return variant;

        // Name with variant stripped: "Rotation (Successive)" → "Rotation"
        var paren = name.IndexOf(" (", StringComparison.Ordinal);
        if (paren > 0)
        {
            var baseName = name[..paren];
            var variantName = name[(paren + 2)..].TrimEnd(')');
            var matches = _catalog!.Nodes
                .Where(n => n.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase)
                         && n.FullName.Contains($"({variantName})", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count > 0) return matches;
        }

        return _catalog!.Nodes
            .Where(n => n.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToList();
    }

    /// <summary>Find nodes by full name (Category.Name), optionally scoped to a package.</summary>
    public List<VvvvNode> GetByFullName(string fullName, string? package = null)
    {
        EnsureLoaded();
        if (!_fullNameIndex.TryGetValue(fullName, out var nodes))
            return [];

        if (package is not null)
            return nodes.Where(n => n.Source.Equals(package, StringComparison.OrdinalIgnoreCase)).ToList();

        return nodes;
    }

    public List<string> GetCategories(string? prefix = null)
    {
        EnsureLoaded();
        var categories = _categoryIndex.Keys.AsEnumerable();

        if (!string.IsNullOrEmpty(prefix))
        {
            categories = categories.Where(c =>
                c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        return categories.OrderBy(c => c).ToList();
    }

    public List<string> GetPackages()
    {
        EnsureLoaded();
        return _packageIndex.Keys.OrderBy(n => n).ToList();
    }

    public CatalogStats GetStats()
    {
        EnsureLoaded();

        var nodesByPackage = _packageIndex
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count)
            .OrderByDescending(kvp => kvp.Value)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        var topCategories = _categoryIndex
            .OrderByDescending(kvp => kvp.Value.Count)
            .Take(20)
            .Select(kvp => $"{kvp.Key} ({kvp.Value.Count})")
            .ToList();

        return new CatalogStats(
            TotalNodes: _catalog!.Nodes.Count,
            TotalPackages: _packageIndex.Count,
            TotalCategories: _categoryIndex.Count,
            TopCategories: topCategories,
            NodesByPackage: nodesByPackage,
            NodesByType: _catalog.NodesByType
        );
    }

    private void EnsureLoaded()
    {
        if (_catalog is null)
            throw new InvalidOperationException("Node catalog not loaded. Call LoadAsync first.");
    }

    private static bool TryNormalizeCategory(string category, out string normalizedCategory)
    {
        normalizedCategory = string.Empty;

        if (string.IsNullOrWhiteSpace(category))
            return false;

        category = category.Trim();

        if (category.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return false;

        if (category.Contains(','))
            return false;

        if (!Regex.IsMatch(category, @"^[A-Za-z0-9][A-Za-z0-9._\- ]*$"))
            return false;

        normalizedCategory = category;
        return true;
    }
}
