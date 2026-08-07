using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace VvvvMcp.Core.Services;

/// <summary>
/// Builds a whole connected subgraph in ONE pass: resolves nodes (live registry
/// first, catalog fallback), adds missing NuGet dependencies, declares all pins,
/// auto-layouts by dataflow depth, wires links (including pin-group auto-indexing
/// and links into pre-existing pins), sets values, saves once, then verifies via
/// the bridge (reload + compile errors).
///
/// This replaces the old add_node × N + connect_pins × M + read_patch workflow
/// (~20 tool calls for a small scene) with a single call.
/// </summary>
public class PatchBuilderService
{
    private static readonly XNamespace PropNs = "property";

    private readonly PatchWriterService _writer;
    private readonly NodeResolutionService _resolver;
    private readonly BridgeClientService _bridge;
    private readonly ILogger<PatchBuilderService> _logger;

    public PatchBuilderService(
        PatchWriterService writer,
        NodeResolutionService resolver,
        BridgeClientService bridge,
        ILogger<PatchBuilderService> logger)
    {
        _writer = writer;
        _resolver = resolver;
        _bridge = bridge;
        _logger = logger;
    }

    // ── Spec DTOs ─────────────────────────────────────────────────────────────

    public class BuildSpec
    {
        public string FilePath { get; set; } = "";
        public List<NodeSpec> Nodes { get; set; } = new();
        public List<PadSpec> Pads { get; set; } = new();
        public List<LinkSpec> Links { get; set; } = new();
        /// <summary>After saving: touch+reload in vvvv and report compile errors.</summary>
        public bool Verify { get; set; } = true;
        /// <summary>Open/focus the document in the running vvvv editor.</summary>
        public bool Open { get; set; } = false;
        /// <summary>"compact" (ids of linked pins only) or "full" (all pin ids).</summary>
        public string Verbosity { get; set; } = "compact";
    }

    public class NodeSpec
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Category { get; set; }
        public string? Package { get; set; }
        /// <summary>"Process" or "Operation" — normally auto-detected, override only if resolution is wrong.</summary>
        public string? Kind { get; set; }
        public string? Bounds { get; set; }
        /// <summary>Pin default values: { "Angular Speed": "0.25, 0, 0" }</summary>
        public Dictionary<string, string>? Values { get; set; }
        /// <summary>Explicit pin subset to declare. Default: all pins from the resolved description.</summary>
        public List<string>? Pins { get; set; }
    }

    public class PadSpec
    {
        public string Key { get; set; } = "";
        public string Type { get; set; } = "Float32";
        public string? Value { get; set; }
        public string? Bounds { get; set; }
        public string? Comment { get; set; }
    }

    public class LinkSpec
    {
        /// <summary>"key.Pin Name", "key" (first output) or an existing 22-char pin/pad id.</summary>
        public string From { get; set; } = "";
        /// <summary>"key.Pin Name", "key" (first input) or an existing pin id. Pin groups auto-index ("Child" → "Child 2").</summary>
        public string To { get; set; } = "";
    }

    // ── Build ─────────────────────────────────────────────────────────────────

    public async Task<object> BuildAsync(string specJson, CancellationToken ct = default)
    {
        BuildSpec spec;
        try
        {
            spec = JsonSerializer.Deserialize<BuildSpec>(specJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) ?? throw new InvalidOperationException("Empty spec");
        }
        catch (JsonException ex)
        {
            return new { success = false, error = $"Invalid spec JSON: {ex.Message}" };
        }

        if (string.IsNullOrWhiteSpace(spec.FilePath))
            return new { success = false, error = "filePath is required" };

        // ── 1. Load or create the document ───────────────────────────────────
        XDocument doc;
        var created = false;
        if (File.Exists(spec.FilePath))
        {
            doc = _writer.LoadDocument(spec.FilePath);
        }
        else
        {
            // File doesn't exist on disk yet. vvvv may already have it open as an
            // unsaved new document (the user created it in the editor but hasn't
            // pressed Ctrl+S yet). If we call CreateDocument() it generates a fresh
            // document ID that differs from vvvv's in-memory document. When we then
            // call ReloadAsync, vvvv sees an ID mismatch and silently refuses to update
            // the editor — the nodes end up in the file but are never shown.
            //
            // Fix: ask the bridge to save the in-memory document first. That writes the
            // file with vvvv's own document ID. We then load THAT file so our edits
            // carry the correct ID and ReloadAsync works normally.
            // If the bridge isn't available or vvvv doesn't have that document open,
            // the save returns a failure and we fall back to CreateDocument().
            var preSaved = false;
            if (await _bridge.CheckAvailabilityAsync())
            {
                var saveResult = await _bridge.SaveDocumentAsync(spec.FilePath);
                if (saveResult?.Success == true && File.Exists(spec.FilePath))
                    preSaved = true;
            }

            if (preSaved)
                doc = _writer.LoadDocument(spec.FilePath);
            else
            {
                doc = _writer.CreateDocument();
                created = true;
            }
        }

        // ── 2. Resolve all nodes (atomic: any failure aborts before writing) ──
        var resolved = new Dictionary<string, ResolvedNode>();
        var resolutionErrors = new List<object>();
        foreach (var n in spec.Nodes)
        {
            if (string.IsNullOrWhiteSpace(n.Key) || string.IsNullOrWhiteSpace(n.Name))
            {
                resolutionErrors.Add(new { node = n.Key, error = "key and name are required" });
                continue;
            }
            var r = await _resolver.ResolveAsync(n.Name, n.Category, n.Package, ct);
            if (!r.Found)
            {
                resolutionErrors.Add(new
                {
                    node = n.Key,
                    requested = n.Name,
                    category = n.Category,
                    error = "Node not found in live registry or catalog",
                    suggestions = r.Suggestions
                });
            }
            else
            {
                resolved[n.Key] = r.Node!;
            }
        }

        if (resolutionErrors.Count > 0)
        {
            return new
            {
                success = false,
                phase = "resolve",
                error = "Some nodes could not be resolved — nothing was written.",
                resolutionErrors
            };
        }

        // ── 3. Dependencies ──────────────────────────────────────────────────
        var depsAdded = new List<string>();
        foreach (var pkg in resolved.Values.Select(r => r.Package).Where(p => p.Length > 0).Distinct())
        {
            if (!HasDependency(doc, pkg))
            {
                _writer.AddDependency(doc, pkg);
                depsAdded.Add(pkg);
            }
        }

        // Warn about packages that are referenced but not installed/loaded in vvvv
        // (nodes resolved from the offline catalog may come from packs vvvv doesn't have).
        var depsNotLoaded = new List<string>();
        if (depsAdded.Count > 0 && await _bridge.CheckAvailabilityAsync())
        {
            var loaded = await _bridge.GetPackagesAsync();
            if (loaded is not null)
            {
                var loadedIds = loaded.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var pkg in depsAdded.Where(p => !loadedIds.Contains(p)))
                    depsNotLoaded.Add(pkg);
            }
        }

        // ── 4. Layout ────────────────────────────────────────────────────────
        var bounds = ComputeLayout(spec, resolved);

        // ── 5. Emit nodes ────────────────────────────────────────────────────
        var nodeIds = new Dictionary<string, string>();
        var pinIds = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var pinKinds = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase); // pinName -> InputPin/OutputPin
        var optionalPinElements = new Dictionary<string, (XElement El, bool HasValue)>(); // pinId -> element (for post-link hiding)

        foreach (var n in spec.Nodes)
        {
            var r = resolved[n.Key];
            var kind = n.Kind is not null
                ? (n.Kind.Equals("Process", StringComparison.OrdinalIgnoreCase) ? "ProcessAppFlag" : "OperationCallFlag")
                : r.XmlNodeKind;

            // Pins to declare: explicit subset or all from description.
            // NOTE: pin-group base pins (e.g. "Child" on RootScene) carry Visibility=Hidden
            // in the symbol data, but their group INSTANCES are visible by default in vvvv —
            // never hide pin-group pins.
            var pinsToDeclare = new List<(string Name, string Kind, bool Optional, bool StateOut)>();
            string HideAware(Models.NodePin p, string kind) =>
                p.IsHidden && !p.IsPinGroup ? kind + ":hidden" : kind;
            if (n.Pins is { Count: > 0 })
            {
                foreach (var p in n.Pins)
                {
                    var inDesc = r.Inputs.FirstOrDefault(d => d.Name.Equals(p, StringComparison.OrdinalIgnoreCase));
                    var outDesc = r.Outputs.FirstOrDefault(d => d.Name.Equals(p, StringComparison.OrdinalIgnoreCase));
                    pinsToDeclare.Add((inDesc?.Name ?? outDesc?.Name ?? p,
                        inDesc is not null ? HideAware(inDesc, "InputPin") : HideAware(outDesc!, "OutputPin"),
                        (inDesc?.IsOptional ?? outDesc?.IsOptional ?? false) && !(inDesc?.IsPinGroup ?? outDesc?.IsPinGroup ?? false),
                        outDesc is not null && IsStateOutput(outDesc, r)));
                }
            }
            else
            {
                if (kind == "ProcessAppFlag")
                    pinsToDeclare.Add(("Node Context", "InputPin:hidden", false, false));
                // Declare all pins; hidden-by-default ones get IsHidden so the
                // patch looks exactly like a hand-placed node in vvvv.
                pinsToDeclare.AddRange(r.Inputs.Select(p => (p.Name, HideAware(p, "InputPin"), p.IsOptional && !p.IsPinGroup, false)));
                pinsToDeclare.AddRange(r.Outputs.Select(p => (p.Name, HideAware(p, "OutputPin"), p.IsOptional && !p.IsPinGroup, IsStateOutput(p, r))));
            }

            var nodeId = VlIdGenerator.NewId();
            var pinIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pinKindMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var nodeRef = new XElement(PropNs + "NodeReference",
                new XAttribute("LastCategoryFullName", r.Category),
                new XAttribute("LastDependency", r.DependencyFile),
                new XElement("Choice",
                    new XAttribute("Kind", "NodeFlag"),
                    new XAttribute("Name", "Node"),
                    new XAttribute("Fixed", "true")),
                new XElement("Choice",
                    new XAttribute("Kind", kind),
                    new XAttribute("Name", r.Name)));

            var nodeEl = new XElement("Node",
                new XAttribute("Bounds", bounds[n.Key]),
                new XAttribute("Id", nodeId),
                nodeRef);

            foreach (var (pinName, pinKindRaw, optional, stateOut) in pinsToDeclare)
            {
                var hidden = pinKindRaw.EndsWith(":hidden");
                var pinKind = hidden ? pinKindRaw[..^7] : pinKindRaw;
                var pinId = VlIdGenerator.NewId();
                pinIdMap[pinName] = pinId;
                pinKindMap[pinName] = pinKind;

                var pinEl = new XElement("Pin",
                    new XAttribute("Id", pinId),
                    new XAttribute("Name", pinName),
                    new XAttribute("Kind", pinKind));
                if (hidden)
                    pinEl.Add(new XAttribute("IsHidden", "true"));

                // Pin default values
                string? pinValue = null;
                var hasValue = n.Values is not null && n.Values.TryGetValue(pinName, out pinValue);
                if (hasValue)
                    pinEl.Add(new XAttribute("DefaultValue", pinValue));

                // Track optional + state-output pins: vvvv hides them when unlinked
                if ((optional || stateOut) && !hidden)
                    optionalPinElements[pinId] = (pinEl, hasValue);

                nodeEl.Add(pinEl);
            }

            GetMainCanvas(doc).Add(nodeEl);
            nodeIds[n.Key] = nodeId;
            pinIds[n.Key] = pinIdMap;
            pinKinds[n.Key] = pinKindMap;
        }

        // ── 6. Emit pads ─────────────────────────────────────────────────────
        var padIds = new Dictionary<string, string>();
        foreach (var p in spec.Pads)
        {
            var padId = VlIdGenerator.NewId();
            var (typeCat, typeDep) = TypeCategoryFor(p.Type);
            var padEl = new XElement("Pad",
                new XAttribute("Id", padId),
                new XAttribute("Comment", p.Comment ?? ""),
                new XAttribute("Bounds", bounds.TryGetValue(p.Key, out var pb) ? pb : "300,100,35,15"),
                new XAttribute("ShowValueBox", "true"),
                new XAttribute("isIOBox", "true"),
                new XElement(PropNs + "TypeAnnotation",
                    new XAttribute("LastCategoryFullName", typeCat),
                    new XAttribute("LastDependency", typeDep),
                    new XElement("Choice",
                        new XAttribute("Kind", "TypeFlag"),
                        new XAttribute("Name", p.Type))));
            if (p.Value is not null)
                padEl.Add(new XAttribute("Value", p.Value));

            GetMainCanvas(doc).Add(padEl);
            padIds[p.Key] = padId;
        }

        // ── 7. Links ─────────────────────────────────────────────────────────
        var appPatch = GetApplicationPatch(doc);
        var linksCreated = 0;
        var linkErrors = new List<object>();
        var usedTargetPins = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var pendingGroupPins = new List<(string NodeId, string PinName, string PinId)>();
        var linkedPinIds = new HashSet<string>();

        foreach (var link in spec.Links)
        {
            var fromId = ResolveEndpoint(link.From, isSource: true, nodeIds, padIds, pinIds, pinKinds, resolved, usedTargetPins, pendingGroupPins, doc, out var fromErr);
            var toId = ResolveEndpoint(link.To, isSource: false, nodeIds, padIds, pinIds, pinKinds, resolved, usedTargetPins, pendingGroupPins, doc, out var toErr);

            if (fromId is null || toId is null)
            {
                linkErrors.Add(new
                {
                    link = $"{link.From} -> {link.To}",
                    error = fromErr ?? toErr
                });
                continue;
            }

            appPatch.Add(new XElement("Link",
                new XAttribute("Id", VlIdGenerator.NewId()),
                new XAttribute("Ids", $"{fromId},{toId}")));
            linksCreated++;
            linkedPinIds.Add(fromId);
            linkedPinIds.Add(toId);
        }

        // Pin-group instances allocated during link resolution ("Child 2", …)
        // need their Pin elements emitted before saving.
        foreach (var (nodeId, pinName, pinId) in pendingGroupPins)
        {
            var nodeEl = doc.Root!.Descendants("Node")
                .FirstOrDefault(n => n.Attribute("Id")?.Value == nodeId);
            nodeEl?.Add(new XElement("Pin",
                new XAttribute("Id", pinId),
                new XAttribute("Name", pinName),
                new XAttribute("Kind", "InputPin")));
        }

        // vvvv hides optional pins that are neither linked nor assigned a value
        foreach (var (pinId, (el, hasValue)) in optionalPinElements)
        {
            if (!hasValue && !linkedPinIds.Contains(pinId))
                el.Add(new XAttribute("IsHidden", "true"));
        }

        // ── 8. Save once ─────────────────────────────────────────────────────
        _writer.SaveDocument(doc, spec.FilePath);

        // ── 9. Verify via bridge ─────────────────────────────────────────────
        object? verification = null;
        var bridgeUp = await _bridge.CheckAvailabilityAsync();
        if (bridgeUp)
        {
            // Touch so vvvv's file watcher definitely notices
            try { File.SetLastWriteTimeUtc(spec.FilePath, DateTime.UtcNow); } catch { }

            if (spec.Verify || spec.Open)
            {
                // Compile errors only exist for documents loaded in the session —
                // load (and show) the document, then poll until its compilation settles.
                await _bridge.OpenDocumentAsync(spec.FilePath);
                await _bridge.ReloadFileAsync(spec.FilePath);
            }

            if (spec.Verify)
            {
                var docId = doc.Root?.Attribute("Id")?.Value;
                List<BridgeErrorInfo>? errors = null;
                var lastCount = -1;
                var stableRounds = 0;

                // Poll up to ~8s; compilation of a freshly opened doc takes 1-3s.
                for (var i = 0; i < 16; i++)
                {
                    await Task.Delay(500, ct);
                    errors = await _bridge.GetErrorsAsync();
                    var count = FilterDocErrors(errors, docId).Count();
                    if (count == lastCount) stableRounds++; else stableRounds = 0;
                    lastCount = count;
                    if (stableRounds >= 2 && i >= 3) break; // stable result
                }

                if (errors is not null)
                {
                    var errs = FilterDocErrors(errors, docId)
                        .Where(e => e.Severity?.Contains("Error", StringComparison.OrdinalIgnoreCase) ?? true)
                        .Take(5)
                        .Select(e => new
                        {
                            e.Message,
                            e.Why,
                            e.ElementId,
                            e.Source,
                            nodeKey = nodeIds.FirstOrDefault(kv => kv.Value.Equals(e.ElementId, StringComparison.OrdinalIgnoreCase)).Key
                                 ?? pinIds.SelectMany(kv => kv.Value)
                                      .FirstOrDefault(p => p.Value.Equals(e.ElementId, StringComparison.OrdinalIgnoreCase)).Key
                        })
                        .ToList();
                    verification = new
                    {
                        compileErrors = errs.Count,
                        errors = errs,
                        hint = errs.Count > 0
                            ? "Fix the spec and re-run build_patch — it is safe to re-run on the same file (remove broken nodes first with remove_node if needed)."
                            : "No compile errors."
                    };
                }
            }
        }

        // ── 10. Compact result ───────────────────────────────────────────────
        var full = spec.VerosityFull();
        return new
        {
            success = linkErrors.Count == 0,
            created,
            filePath = spec.FilePath,
            nodes = spec.Nodes.Select(n =>
            {
                var r = resolved[n.Key];
                var result = new Dictionary<string, object?>
                {
                    ["key"] = n.Key,
                    ["id"] = nodeIds[n.Key],
                    ["node"] = r.FullName,
                    ["kind"] = r.Kind,
                    ["origin"] = r.Origin
                };
                if (full)
                    result["pins"] = pinIds[n.Key];
                else
                    result["pins"] = pinIds[n.Key]
                        .Where(kv => linkedPinIds.Contains(kv.Value))
                        .ToDictionary(kv => kv.Key, kv => kv.Value);
                return result;
            }).ToList(),
            pads = padIds.Select(kv => new { key = kv.Key, id = kv.Value }).ToList(),
            linksCreated,
            linkErrors = linkErrors.Count > 0 ? linkErrors : null,
            dependenciesAdded = depsAdded.Count > 0 ? depsAdded : null,
            dependenciesNotLoaded = depsNotLoaded.Count > 0 ? depsNotLoaded : null,
            dependenciesNotLoadedHint = depsNotLoaded.Count > 0
                ? "These packages are not loaded in the running vvvv. Install via: nuget install <id> (or Document > Dependencies in vvvv, which offers to install missing ones)."
                : null,
            bridge = bridgeUp ? "connected" : "offline (no verify/reload)",
            verification,
            hint = "To wire this subgraph to existing nodes: run build_patch again with only links (nodes: []), referencing these pin ids and the existing pin ids from read_patch."
        };
    }

    /// <summary>
    /// State outputs (the process/class instance itself) are hidden by default in vvvv —
    /// you only show them when operating on the instance. Detected via the IsState flag
    /// from the live symbols, or by name/type heuristics for factory-sourced nodes.
    /// </summary>
    private static bool IsStateOutput(Models.NodePin outputPin, ResolvedNode node)
    {
        if (outputPin.IsState) return true;
        if (outputPin.Name.Equals("State Output", StringComparison.OrdinalIgnoreCase)) return true;
        // Output pin whose type is the node's own type (e.g. "Output: Box" on Box)
        var nodeBaseName = node.Name;
        var paren = nodeBaseName.IndexOf(" (", StringComparison.Ordinal);
        if (paren > 0) nodeBaseName = nodeBaseName[..paren];
        if (outputPin.Type.Equals(nodeBaseName, StringComparison.OrdinalIgnoreCase)
            && node.Kind == "Process")
            return true;
        return false;
    }

    /// <summary>Errors belonging to a document (by Document Id); errors without doc info are included.</summary>
    private static IEnumerable<BridgeErrorInfo> FilterDocErrors(List<BridgeErrorInfo>? errors, string? docId)
    {
        if (errors is null) return Enumerable.Empty<BridgeErrorInfo>();
        if (string.IsNullOrEmpty(docId) || !errors.Any(e => e.DocumentId is not null))
            return errors;
        return errors.Where(e =>
            e.DocumentId is null ||
            e.DocumentId.Equals(docId, StringComparison.OrdinalIgnoreCase));
    }

    // ── Endpoint resolution ───────────────────────────────────────────────────

    private string? ResolveEndpoint(
        string endpoint, bool isSource,
        Dictionary<string, string> nodeIds,
        Dictionary<string, string> padIds,
        Dictionary<string, Dictionary<string, string>> pinIds,
        Dictionary<string, Dictionary<string, string>> pinKinds,
        Dictionary<string, ResolvedNode> resolved,
        Dictionary<string, HashSet<string>> usedTargetPins,
        List<(string NodeId, string PinName, string PinId)> pendingGroupPins,
        XDocument doc,
        out string? error)
    {
        error = null;
        endpoint = endpoint.Trim();

        var dot = endpoint.IndexOf('.');
        var key = dot < 0 ? endpoint : endpoint[..dot];
        var pinName = dot < 0 ? null : endpoint[(dot + 1)..];

        // Pad as source
        if (padIds.TryGetValue(key, out var padId))
        {
            if (!isSource) { error = $"Pad '{key}' cannot be a link target (use a node input pin)"; return null; }
            return padId;
        }

        if (!nodeIds.TryGetValue(key, out var nodeId))
        {
            // Not a spec key — two fallbacks:
            // 1. Raw 22-char pin/pad id with no dot (from read_patch output) → use as-is.
            // 2. nodeId.PinName format where the node is already in the document (follow-up
            //    build_patch calls that reference nodes from a previous call by their XML Id).
            if (dot < 0 && endpoint.Length >= 15)
                return endpoint;

            if (dot > 0 && key.Length >= 15)
            {
                // Look up the node in the existing document and find the pin by name
                var existingNode = doc.Root!.Descendants("Node")
                    .FirstOrDefault(n => n.Attribute("Id")?.Value == key);
                if (existingNode is not null)
                {
                    var pin = existingNode.Elements("Pin")
                        .FirstOrDefault(p => p.Attribute("Name")?.Value?.Equals(pinName, StringComparison.OrdinalIgnoreCase) == true);
                    if (pin is not null)
                        return pin.Attribute("Id")?.Value;
                    // List available pins to help the caller
                    var existingPins = existingNode.Elements("Pin")
                        .Select(p => p.Attribute("Name")?.Value)
                        .Where(n => n is not null)
                        .ToList();
                    error = $"Pin '{pinName}' not found on existing node '{key}'. Available: {string.Join(", ", existingPins)}";
                    return null;
                }
            }

            error = $"'{key}' is neither a node/pad key from this spec nor a known pin id";
            return null;
        }

        var pins = pinIds[key];
        var kinds = pinKinds[key];

        // No pin specified: first output (source) or first input (target)
        if (pinName is null)
        {
            var wanted = isSource ? "OutputPin" : "InputPin";
            var candidate = pins.Keys.FirstOrDefault(p =>
                kinds.TryGetValue(p, out var k) && k == wanted &&
                !p.Equals("Node Context", StringComparison.OrdinalIgnoreCase));
            if (candidate is null)
            {
                error = $"Node '{key}' has no {(isSource ? "output" : "input")} pins";
                return null;
            }
            if (!isSource) MarkUsed(nodeId, candidate);
            return pins[candidate];
        }

        // Exact pin
        if (pins.TryGetValue(pinName, out var exactId))
        {
            if (isSource)
                return exactId; // outputs can feed any number of links

            // Target already wired → pin-group auto-indexing: "Child" → "Child 2" → …
            if (usedTargetPins.TryGetValue(nodeId, out var used) && used.Contains(pinName))
            {
                var idx = 2;
                string candidate;
                while (used.Contains(candidate = $"{pinName} {idx}")) idx++;
                return AllocateGroupPin(nodeId, candidate, pins, kinds);
            }
            MarkUsed(nodeId, pinName);
            return exactId;
        }

        // Unknown pin name — maybe a pin-group instance not yet declared ("Child 2")
        if (!isSource && resolved.TryGetValue(key, out var r) &&
            r.Inputs.Any(p => pinName.StartsWith(p.Name + " ", StringComparison.OrdinalIgnoreCase) ||
                              p.Name.Equals(pinName, StringComparison.OrdinalIgnoreCase)))
        {
            return AllocateGroupPin(nodeId, pinName, pins, kinds);
        }

        // Suggest available pins
        var available = string.Join(", ", pins.Keys.Where(p => !p.Equals("Node Context", StringComparison.OrdinalIgnoreCase)));
        error = $"Pin '{pinName}' not found on '{key}'. Available: {available}";
        return null;

        void MarkUsed(string nid, string pn)
        {
            if (!usedTargetPins.TryGetValue(nid, out var set))
                usedTargetPins[nid] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(pn);
        }

        string AllocateGroupPin(string nid, string newPinName,
            Dictionary<string, string> pins, Dictionary<string, string> kinds)
        {
            var pinId = VlIdGenerator.NewId();
            pins[newPinName] = pinId;
            kinds[newPinName] = "InputPin";
            MarkUsed(nid, newPinName);
            pendingGroupPins.Add((nid, newPinName, pinId));
            return pinId;
        }
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    private Dictionary<string, string> ComputeLayout(BuildSpec spec, Dictionary<string, ResolvedNode> resolved)
    {
        var result = new Dictionary<string, string>();
        var explicitBounds = new HashSet<string>();

        foreach (var n in spec.Nodes.Where(n => n.Bounds is not null))
        {
            result[n.Key] = n.Bounds!;
            explicitBounds.Add(n.Key);
        }
        foreach (var p in spec.Pads.Where(p => p.Bounds is not null))
        {
            result[p.Key] = p.Bounds!;
            explicitBounds.Add(p.Key);
        }

        // Topological depth per key (longest path from a source)
        var keys = spec.Nodes.Select(n => n.Key).Concat(spec.Pads.Select(p => p.Key)).ToHashSet();
        var depth = keys.ToDictionary(k => k, _ => 0);
        for (int i = 0; i < keys.Count; i++)
        {
            foreach (var l in spec.Links)
            {
                var from = l.From.Split('.')[0];
                var to = l.To.Split('.')[0];
                if (keys.Contains(from) && keys.Contains(to) && depth[to] < depth[from] + 1)
                    depth[to] = depth[from] + 1;
            }
        }

        // vvvv node Bounds: height is ALWAYS 19 (header only — vvvv renders pin rows
        // below it automatically). Width is derived from the node name and the
        // visible pin rows (input name left + output name right share a row).
        // For layout spacing we still need the VISUAL height (header + pin rows)
        // so stacked nodes don't overlap.
        int VisualHeightOf(string key)
        {
            if (!resolved.TryGetValue(key, out var r)) return 30; // pads
            var visibleInputs = r.Inputs.Count(p => !p.IsHidden);
            var visibleOutputs = r.Outputs.Count(p => !p.IsHidden);
            return 19 + Math.Max(1, Math.Max(visibleInputs, visibleOutputs)) * 15;
        }
        int WidthOf(string key)
        {
            if (!resolved.TryGetValue(key, out var r)) return 90; // pads
            var longestIn = r.Inputs.Where(p => !p.IsHidden).Select(p => p.Name.Length).DefaultIfEmpty(0).Max();
            var longestOut = r.Outputs.Where(p => !p.IsHidden).Select(p => p.Name.Length).DefaultIfEmpty(0).Max();
            var pinRowWidth = (longestIn + longestOut) * 7 + (longestOut > 0 ? 60 : 30);
            var nameWidth = r.Name.Length * 8 + 40;
            return Math.Max(90, Math.Max(nameWidth, pinRowWidth));
        }

        // Column x positions from cumulative column widths
        var maxDepth = keys.Count > 0 ? keys.Max(k => depth[k]) : 0;
        var columnX = new int[maxDepth + 1];
        var x = 250;
        for (var d = 0; d <= maxDepth; d++)
        {
            columnX[d] = x;
            var colWidth = keys.Where(k => depth[k] == d).Select(WidthOf).DefaultIfEmpty(160).Max();
            x += colWidth + 90; // gutter for links
        }

        // Stack nodes per column with cumulative VISUAL heights (pin rows included),
        // but write Bounds with the vvvv-correct constant height of 19.
        var columnY = new Dictionary<int, int>();
        foreach (var k in keys.OrderBy(k => depth[k]).ThenBy(k => k, StringComparer.Ordinal))
        {
            if (explicitBounds.Contains(k)) continue;
            var d = depth[k];
            var y = columnY.TryGetValue(d, out var cy) ? cy : 120;
            var isNode = resolved.ContainsKey(k);
            result[k] = isNode
                ? $"{columnX[d]},{y},{WidthOf(k)},19"
                : $"{columnX[d]},{y}";
            columnY[d] = y + VisualHeightOf(k) + 45;
        }

        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool HasDependency(XDocument doc, string package)
    {
        return doc.Root!.Elements("NugetDependency")
            .Any(d => string.Equals(d.Attribute("Location")?.Value, package, StringComparison.OrdinalIgnoreCase));
    }

    private static XElement GetMainCanvas(XDocument doc)
    {
        var appNode = doc.Root!.Descendants("Node")
            .FirstOrDefault(n => n.Attribute("Name")?.Value == "Application")
            ?? throw new InvalidOperationException("No Application node found");
        return appNode.Descendants("Canvas")
            .FirstOrDefault(c => c.Attribute("CanvasType")?.Value == "Group")
            ?? throw new InvalidOperationException("No Group canvas found inside Application");
    }

    private static XElement GetApplicationPatch(XDocument doc)
    {
        var appNode = doc.Root!.Descendants("Node")
            .FirstOrDefault(n => n.Attribute("Name")?.Value == "Application")
            ?? throw new InvalidOperationException("No Application node found");
        return appNode.Elements("Patch").FirstOrDefault()
            ?? throw new InvalidOperationException("No inner Patch found in Application node");
    }

    private static (string Category, string Dependency) TypeCategoryFor(string typeName)
    {
        var baseName = typeName;
        // Spread<Float32> → Float32
        var lt = baseName.IndexOf('<');
        if (baseName.StartsWith("Spread<") && lt > 0)
            baseName = baseName[(lt + 1)..].TrimEnd('>');

        return baseName switch
        {
            "Vector2" => ("2D", "VL.CoreLib.vl"),
            "Vector3" => ("3D", "VL.CoreLib.vl"),
            "Vector4" => ("4D", "VL.CoreLib.vl"),
            "Matrix" => ("3D.Transform", "VL.CoreLib.vl"),
            "RGBA" or "RGB" or "HSV" or "HSL" => ("Color", "VL.CoreLib.vl"),
            _ => ("Primitive", "VL.CoreLib.vl")
        };
    }
}

internal static class BuildSpecExtensions
{
    public static bool VerosityFull(this PatchBuilderService.BuildSpec s) =>
        s.Verbosity.Equals("full", StringComparison.OrdinalIgnoreCase);
}
