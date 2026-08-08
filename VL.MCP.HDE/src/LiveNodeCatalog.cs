using System.Collections;
using System.Reflection;
using VL.Core;

namespace VL.MCP;

/// <summary>
/// Live node catalog harvested from the running vvvv instance's NodeFactoryRegistry.
/// This is ground truth: every node the user can actually place, with resolved
/// pin names, real System.Type-based pin types, default values and source package.
///
/// The registry is read via reflection (VL.Lang is not referenced at compile time).
/// A snapshot is built once on the vvvv main loop thread (thread-safe-ish for VL
/// internals) and served to HTTP handler threads. Rebuild on demand via ?refresh=1.
/// </summary>
internal class LiveNodeCatalog
{
    private readonly object _gate = new();
    private List<LiveNode> _nodes = new();
    private List<(string Id, string Version, string Path)> _packages = new();
    private bool _buildRequested = true;
    private bool _building;
    private string? _lastBuildError;
    private DateTime _builtAt = DateTime.MinValue;

    public int NodeCount { get { lock (_gate) return _nodes.Count; } }
    public DateTime BuiltAt => _builtAt;
    public string? LastBuildError => _lastBuildError;
    public bool IsStale => _builtAt == DateTime.MinValue || (DateTime.UtcNow - _builtAt) > TimeSpan.FromMinutes(10);

    /// <summary>Per-factory diagnostics from the last build (factory id → node count).</summary>
    public object? Diagnostics { get; private set; }

    /// <summary>Ask for a rebuild on the next Update tick (main loop thread).</summary>
    public void RequestRebuild() => _buildRequested = true;

    private int _lastFactoryCount = -1;
    private int _framesSinceChange;

    /// <summary>Called from MCPBridgeServer.Update — runs on the vvvv main loop thread.</summary>
    public void Update(NodeContext? nodeContext)
    {
        if (nodeContext is null || _building) return;

        // Auto-rebuild when the factory set changes (packages load lazily as
        // documents reference them — new factories appear minutes after startup).
        // Debounced: wait ~90 frames of stability before rebuilding.
        if (!_buildRequested)
        {
            var count = TryGetFactoryCount(nodeContext);
            if (count >= 0 && count != _lastFactoryCount)
            {
                _lastFactoryCount = count;
                _framesSinceChange = 0;
            }
            else if (count >= 0 && ++_framesSinceChange > 90 && ShouldAutoRebuild(count))
            {
                _framesSinceChange = int.MinValue; // don't retrigger until next change
                _buildRequested = true;
            }
        }

        if (!_buildRequested) return;
        _buildRequested = false;
        _building = true;

        // Build on a threadpool thread — the PreCompilation snapshot is immutable,
        // and reading symbols off the main thread keeps the vvvv UI responsive
        // (a full pass over all loaded packages takes a few seconds).
        var ctx = nodeContext;
        Task.Run(() =>
        {
            try
            {
                Build(ctx);
                _lastBuildError = null;
                _builtAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _lastBuildError = ex.Message;
            }
            finally
            {
                _building = false;
            }
        });
    }

    private bool ShouldAutoRebuild(int factoryCount)
    {
        // Never built → build. Factory count grew since last build → rebuild.
        if (_builtAt == DateTime.MinValue) return true;
        return factoryCount != _factoryCountAtBuild;
    }

    private int _factoryCountAtBuild = -1;

    private static int TryGetFactoryCount(NodeContext nodeContext)
    {
        try
        {
            var session = GetSession();
            if (session is null) return -1;
            var registry = session.GetType().GetProperty("NodeFactoryRegistry")?.GetValue(session);
            if (registry is null) return -1;
            var factories = registry.GetType().GetProperty("Factories")?.GetValue(registry) as IEnumerable;
            if (factories is null) return -1;
            var count = 0;
            foreach (var _ in factories) count++;
            return count;
        }
        catch { return -1; }
    }

    // ── Snapshot build (reflection over VL.Lang) ──────────────────────────────

    private void Build(NodeContext nodeContext)
    {
        var session = GetSession();
        if (session is null) throw new InvalidOperationException("VLSession not available");
        var sessionType = session.GetType();

        // Packages: Id, Version, PackagePath — used to map node FilePath → package Id
        var packages = new List<(string Id, string Version, string Path)>();
        var nugets = sessionType.GetProperty("AvailableNugets")?.GetValue(session) as IEnumerable;
        if (nugets is not null)
        {
            foreach (var pkg in nugets)
            {
                try
                {
                    var pt = pkg.GetType();
                    var id = pt.GetProperty("Id")?.GetValue(pkg)?.ToString();
                    var ver = pt.GetProperty("Version")?.GetValue(pkg)?.ToString();
                    var path = pt.GetProperty("PackagePath")?.GetValue(pkg)?.ToString();
                    var isVL = pt.GetProperty("IsVLPackage")?.GetValue(pkg) as bool? ?? false;
                    if (id is not null && path is not null && isVL)
                        packages.Add((id, ver ?? "", path.TrimEnd('\\', '/')));
                }
                catch { }
            }
        }

        // Node factories
        var registry = sessionType.GetProperty("NodeFactoryRegistry")?.GetValue(session)
                    ?? nodeContext.AppHost?.GetType().GetProperty("NodeFactoryRegistry")?.GetValue(nodeContext.AppHost);
        if (registry is null) throw new InvalidOperationException("NodeFactoryRegistry not available");

        var factories = registry.GetType().GetProperty("Factories")?.GetValue(registry) as IEnumerable;
        if (factories is null) throw new InvalidOperationException("Factories not available");

        var nodes = new List<LiveNode>(8000);
        var factoryStats = new List<FactoryStat>();
        foreach (var factory in factories)
        {
            var stat = new FactoryStat();
            try
            {
                var ft = factory.GetType();
                stat.Factory = ft.GetProperty("Identifier")?.GetValue(factory)?.ToString()
                          ?? ft.GetProperty("FilePath")?.GetValue(factory)?.ToString()
                          ?? ft.Name;
                var descriptions = ft.GetProperty("NodeDescriptions")?.GetValue(factory) as IEnumerable;
                if (descriptions is null)
                {
                    stat.Note = "NodeDescriptions null";
                }
                else
                {
                    var before = nodes.Count;
                    foreach (var desc in descriptions)
                    {
                        try
                        {
                            var n = ExtractNode(desc, packages);
                            if (n is not null) nodes.Add(n);
                        }
                        catch { }
                    }
                    stat.Nodes = nodes.Count - before;
                }
            }
            catch (Exception ex)
            {
                stat.Error = ex.Message;
            }
            factoryStats.Add(stat);
        }

        Diagnostics = new
        {
            factoryCount = factoryStats.Count,
            factories = factoryStats.OrderByDescending(f => f.Nodes).Take(25).ToList()
        };

        // ── Source 2: VL-defined nodes from the compilation symbols ─────────
        // The NodeFactoryRegistry only covers .NET-backed nodes. VL-defined nodes
        // (processes/classes/operations defined in .vl files — e.g. Box [Stride.Models])
        // live in the PreCompilation symbol graph.
        var symbolStats = new List<FactoryStat>();
        try
        {
            ExtractFromCompilation(session, packages, nodes, symbolStats);
        }
        catch (Exception ex)
        {
            symbolStats.Add(new FactoryStat { Factory = "compilation", Error = ex.Message });
        }

        Diagnostics = new
        {
            factoryCount = factoryStats.Count,
            factories = factoryStats.OrderByDescending(f => f.Nodes).Take(25).ToList(),
            symbolSources = symbolStats.OrderByDescending(s => s.Nodes).Take(25).ToList()
        };

        // Merge duplicates by FullName — prefer entries with more pin info
        var merged = nodes
            .GroupBy(n => n.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(n => n.Inputs.Count + n.Outputs.Count)
                .ThenByDescending(n => n.Inputs.Count(p => p.Type.Length > 0))
                .First())
            .ToList();

        lock (_gate)
        {
            _nodes = merged;
            _packages = packages;
        }
        _factoryCountAtBuild = factoryStats.Count;
    }

    // ── Compilation symbol extraction ─────────────────────────────────────────

    private void ExtractFromCompilation(
        object session,
        List<(string Id, string Version, string Path)> packages,
        List<LiveNode> nodes,
        List<FactoryStat> stats)
    {
        var sessionType = session.GetType();
        var compilation = sessionType.GetProperty("LatestCompilation")?.GetValue(session);
        if (compilation is null) return;

        // DocumentsAndPackages covers BOTH open documents (DocSymbols) and
        // referenced binary packages (CompiledSymbols) — DocumentSymbols alone
        // only contains the open/editable documents.
        var sources = compilation.GetType().GetProperty("DocumentsAndPackages")?.GetValue(compilation) as IEnumerable;
        if (sources is null) return;

        var accessors = new AccessorCache();

        foreach (var source in sources)
        {
            var stat = new FactoryStat();
            try
            {
                var st = source.GetType();
                var filePath = accessors.Prop(st, "FilePath")?.GetValue(source)?.ToString() ?? "";
                stat.Factory = Path.GetFileName(filePath);

                // Prefer the PackageInfo id over path guessing
                var package = "";
                var pkgObj = accessors.Prop(st, "Package")?.GetValue(source);
                if (pkgObj is not null)
                    package = accessors.Prop(pkgObj.GetType(), "Id")?.GetValue(pkgObj)?.ToString() ?? "";
                if (string.IsNullOrEmpty(package))
                    package = ResolvePackage(filePath, packages);

                // DefinedSymbols: ILookup<string, ICategorizableSymbol> — types & node defs
                var defined = accessors.Prop(st, "DefinedSymbols")?.GetValue(source) as IEnumerable;
                if (defined is null) { stat.Note = "no DefinedSymbols"; stats.Add(stat); continue; }

                foreach (var group in defined)
                {
                    IEnumerable? items = group as IEnumerable;
                    if (items is null) continue;
                    foreach (var sym in items)
                    {
                        try
                        {
                            stat.Nodes += ExtractSymbolAndMembers(sym, accessors, package, filePath, nodes);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                stat.Error = ex.Message;
            }
            if (stat.Nodes > 0 || stat.Error is not null)
                stats.Add(stat);
        }
    }

    /// <summary>
    /// Extracts a symbol itself (when it is a node definition — e.g. a process like
    /// Box [Stride.Models]) plus its member operations (OperationDefinitions of a
    /// class/record). Returns the number of nodes added.
    /// </summary>
    private static int ExtractSymbolAndMembers(
        object sym, AccessorCache accessors, string package, string filePath, List<LiveNode> nodes)
    {
        var added = 0;
        var st = sym.GetType();

        // Skip internal/unused symbols (the node browser hides those by default)
        var smell = accessors.Prop(st, "Smell")?.GetValue(sym)?.ToString() ?? "";
        var isInternal = smell.Contains("Internal", StringComparison.OrdinalIgnoreCase);

        if (!isInternal && accessors.Implements(st, "INodeDefinitionSymbol"))
        {
            var node = ExtractNodeFromSymbol(sym, accessors, package, filePath);
            if (node is not null) { nodes.Add(node); added++; }
        }

        // Member operations of types (records/classes): Create, Update, methods…
        if (!isInternal)
        {
            var opDefs = accessors.Prop(st, "OperationDefinitions")?.GetValue(sym) as IEnumerable;
            if (opDefs is not null)
            {
                foreach (var op in opDefs)
                {
                    try
                    {
                        if (!accessors.Implements(op.GetType(), "INodeDefinitionSymbol")) continue;
                        var node = ExtractNodeFromSymbol(op, accessors, package, filePath);
                        if (node is not null) { nodes.Add(node); added++; }
                    }
                    catch { }
                }
            }
        }

        return added;
    }

    private static LiveNode? ExtractNodeFromSymbol(
        object sym, AccessorCache accessors, string package, string filePath)
    {
        var st = sym.GetType();

        // Kind: ElementKind enum — ProcessDefinition, OperationDefinition, ClassDefinition…
        var kindName = accessors.Prop(st, "Kind")?.GetValue(sym)?.ToString() ?? "";
        var isProcess = kindName.Contains("Process") || kindName.Contains("Class") || kindName.Contains("Record");
        // Skip pure type forwards / unused symbols
        var isUnused = accessors.Prop(st, "IsUnused")?.GetValue(sym) as bool? ?? false;
        if (isUnused) return null;

        // ── Smell / visibility ────────────────────────────────────────────────
        // ParentCategory is a CategoryAndSmell: .Category + .Smell
        // Smell values: "" (visible), "Internal" (hidden), "Advanced" (hidden by default),
        // "Obsolete", "Experimental", "Hidden", etc.
        // The node browser hides anything with a non-empty/non-default Smell.
        // We read Smell from BOTH the symbol itself AND its ParentCategory.
        var smell = "";
        var directSmell = accessors.Prop(st, "Smell")?.GetValue(sym)?.ToString() ?? "";
        if (!string.IsNullOrEmpty(directSmell)) smell = directSmell;

        var parentCat = accessors.Prop(st, "ParentCategory")?.GetValue(sym);
        var catSmell = "";
        if (parentCat is not null)
        {
            catSmell = accessors.Prop(parentCat.GetType(), "Smell")?.GetValue(parentCat)?.ToString() ?? "";
            if (string.IsNullOrEmpty(smell) && !string.IsNullOrEmpty(catSmell))
                smell = catSmell;
        }

        // Classify into vvvv's 4-level visibility system:
        //   default → hi-level (shown in default node browser)
        //   advanced → low-level (shown with "show advanced")
        //   experimental → future/unstable
        //   obsolete → deprecated
        //   internal → completely hidden (never placeable)
        var visibility = ClassifyVisibility(smell);

        // Name: try Name (NameAndVersion → FullName keeps "(Variant)"), else Element.Name
        string? name = null;
        var nameObj = accessors.Prop(st, "Name")?.GetValue(sym);
        if (nameObj is not null)
        {
            var fullName = accessors.Prop(nameObj.GetType(), "FullName")?.GetValue(nameObj)?.ToString();
            name = fullName ?? nameObj.ToString();
        }
        if (string.IsNullOrEmpty(name))
        {
            var element = accessors.Prop(st, "Element")?.GetValue(sym);
            if (element is not null)
                name = accessors.Prop(element.GetType(), "Name")?.GetValue(element)?.ToString();
        }
        if (string.IsNullOrEmpty(name)) return null;

        // Category: CategoryAndSmell → Category → FullName,
        // then Category.FullName, then ContainingType fallbacks
        var category = "";
        if (parentCat is not null)
        {
            var catObj = accessors.Prop(parentCat.GetType(), "Category")?.GetValue(parentCat) ?? parentCat;
            category = accessors.Prop(catObj.GetType(), "FullName")?.GetValue(catObj)?.ToString()
                    ?? catObj.ToString() ?? "";
        }
        if (string.IsNullOrEmpty(category))
        {
            var catObj = accessors.Prop(st, "Category")?.GetValue(sym);
            if (catObj is not null)
                category = accessors.Prop(catObj.GetType(), "FullName")?.GetValue(catObj)?.ToString()
                        ?? catObj.ToString() ?? "";
        }
        if (string.IsNullOrEmpty(category))
        {
            var containing = accessors.Prop(st, "ContainingType")?.GetValue(sym);
            if (containing is not null)
            {
                var pc = accessors.Prop(containing.GetType(), "ParentCategory")?.GetValue(containing);
                var cc = pc is not null
                    ? accessors.Prop(pc.GetType(), "Category")?.GetValue(pc) ?? pc
                    : accessors.Prop(containing.GetType(), "Category")?.GetValue(containing);
                if (cc is not null)
                    category = accessors.Prop(cc.GetType(), "FullName")?.GetValue(cc)?.ToString() ?? "";
            }
        }

        // Categories with " - Hidden" suffix are internal lifecycle operations
        if (category.Contains(" - Hidden", StringComparison.OrdinalIgnoreCase))
            visibility = "internal";

        // Pins
        var inputs = ExtractSymbolPins(accessors.Prop(st, "Inputs")?.GetValue(sym) as IEnumerable, accessors, isInput: true);
        var outputs = ExtractSymbolPins(accessors.Prop(st, "Outputs")?.GetValue(sym) as IEnumerable, accessors, isInput: false);

        // MemberType distinguishes real nodes from synthesized property/field accessors
        // (PropertyGetter/PropertySetter/FieldGetter/…) — those are demoted in search.
        var memberType = accessors.Prop(st, "MemberType")?.GetValue(sym)?.ToString() ?? "";
        var isAccessor = memberType is "PropertyGetter" or "PropertySetter"
            or "FieldGetter" or "FieldSetter" or "IndexerGetter" or "IndexerSetter";

        var isGeneric = accessors.Prop(st, "IsGeneric")?.GetValue(sym) as bool? ?? false;

        return new LiveNode
        {
            Name = name,
            Category = category,
            FullName = string.IsNullOrEmpty(category) ? name : category + "." + name,
            Kind = isProcess ? "Process" : "Operation",
            Package = package,
            SourceFile = filePath,
            Inputs = inputs.Where(p => p.Name != "Node Context").ToList(),
            Outputs = outputs,
            IsGeneric = isGeneric,
            IsAccessor = isAccessor,
            Visibility = visibility,
            Smell = smell
        };
    }

    /// <summary>
    /// Maps a raw VL Smell string to one of the 4 node browser visibility levels.
    /// </summary>
    private static string ClassifyVisibility(string smell)
    {
        if (string.IsNullOrEmpty(smell) || smell == "Default" || smell == "None" || smell == "0")
            return "default";
        if (smell.Contains("Internal", StringComparison.OrdinalIgnoreCase)
            || smell.Contains("Hidden", StringComparison.OrdinalIgnoreCase))
            return "internal";
        if (smell.Contains("Advanced", StringComparison.OrdinalIgnoreCase))
            return "advanced";
        if (smell.Contains("Experimental", StringComparison.OrdinalIgnoreCase)
            || smell.Contains("Future", StringComparison.OrdinalIgnoreCase))
            return "experimental";
        if (smell.Contains("Obsolete", StringComparison.OrdinalIgnoreCase)
            || smell.Contains("Deprecated", StringComparison.OrdinalIgnoreCase))
            return "obsolete";
        // Unknown non-empty smell → treat as advanced (visible but not default)
        return "advanced";
    }

    private static List<LivePin> ExtractSymbolPins(IEnumerable? pins, AccessorCache accessors, bool isInput)
    {
        var result = new List<LivePin>();
        if (pins is null) return result;

        foreach (var pin in pins)
        {
            try
            {
                var pt = pin.GetType();
                // Pin name lives on the definition's model element
                var def = accessors.Prop(pt, "Definition")?.GetValue(pin) ?? pin;
                var dt = def.GetType();

                string? pname = null;
                var element = accessors.Prop(dt, "Element")?.GetValue(def);
                if (element is not null)
                    pname = accessors.Prop(element.GetType(), "Name")?.GetValue(element)?.ToString();
                pname ??= accessors.Prop(dt, "Name")?.GetValue(def)?.ToString();
                if (string.IsNullOrEmpty(pname)) continue;

                // Visibility: PinVisibility enum (Visible / Optional / Hidden / …)
                var visibility = accessors.Prop(dt, "Visibility")?.GetValue(def)?.ToString() ?? "";
                var isVisible = accessors.Prop(pt, "IsVisible")?.GetValue(pin) as bool? ?? true;
                var hidden = !isVisible || visibility.Contains("Hidden", StringComparison.OrdinalIgnoreCase);

                // Type
                var typeObj = accessors.Prop(dt, "Type")?.GetValue(def) ?? accessors.Prop(pt, "Type")?.GetValue(pin);
                var typeName = TypeSymbolName(typeObj, accessors);

                // Default value: DefaultValue is a CompileTimeValue wrapper — unwrap .Value
                var defValObj = accessors.Prop(dt, "DefaultValue")?.GetValue(def);
                var defVal = "";
                if (defValObj is not null)
                {
                    var inner = accessors.Prop(defValObj.GetType(), "Value")?.GetValue(defValObj);
                    if (inner is not null)
                    {
                        defVal = inner switch
                        {
                            float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            _ => inner.ToString() ?? ""
                        };
                    }
                }
                if (defVal is "No value" or "Empty") defVal = "";

                // Pin group
                var groupKind = accessors.Prop(dt, "PinGroupKind")?.GetValue(def)?.ToString() ?? "";
                var isGroup = groupKind is not ("None" or "" or "0");

                // State pin (instance in/out of class/record/process operations).
                // State OUTPUTS are hidden by default in vvvv — you only show them
                // when you want to operate on the instance itself.
                var isState = (accessors.Prop(pt, "IsState")?.GetValue(pin) as bool?)
                           ?? (accessors.Prop(dt, "IsState")?.GetValue(def) as bool?)
                           ?? false;

                result.Add(new LivePin
                {
                    Name = pname,
                    Type = typeName,
                    DefaultValue = defVal.Length > 60 ? defVal[..60] : defVal,
                    IsPinGroup = isGroup,
                    IsHidden = hidden,
                    IsOptional = visibility.Contains("Optional", StringComparison.OrdinalIgnoreCase),
                    IsState = isState
                });
            }
            catch { }
        }
        return result;
    }

    private static string TypeSymbolName(object? typeSymbol, AccessorCache accessors)
    {
        if (typeSymbol is null) return "";
        try
        {
            var t = typeSymbol.GetType();
            // ITypeSymbol: prefer FullName/Name; ToString as fallback
            var fullName = accessors.Prop(t, "FullName")?.GetValue(typeSymbol)?.ToString();
            if (!string.IsNullOrEmpty(fullName)) return ShortenTypeName(fullName);
            var name = accessors.Prop(t, "Name")?.GetValue(typeSymbol)?.ToString();
            if (!string.IsNullOrEmpty(name)) return name;
            return typeSymbol.ToString() ?? "";
        }
        catch { return ""; }
    }

    /// <summary>"Stride.Core.Mathematics.Vector3" → "Vector3", keep generics readable.</summary>
    private static string ShortenTypeName(string fullName)
    {
        if (fullName.Length == 0) return fullName;
        // Keep generic structure but shorten each segment
        var sb = new System.Text.StringBuilder(fullName.Length);
        var segment = new System.Text.StringBuilder();
        foreach (var c in fullName)
        {
            if (c is '<' or '>' or ',' or ' ')
            {
                sb.Append(ShortenSegment(segment.ToString()));
                segment.Clear();
                sb.Append(c);
            }
            else segment.Append(c);
        }
        sb.Append(ShortenSegment(segment.ToString()));
        return sb.ToString();

        static string ShortenSegment(string s)
        {
            var dot = s.LastIndexOf('.');
            return dot > 0 && dot < s.Length - 1 ? s[(dot + 1)..] : s;
        }
    }

    /// <summary>Caches PropertyInfo lookups and interface checks per runtime type.</summary>
    private class AccessorCache
    {
        private readonly Dictionary<(Type, string), PropertyInfo?> _props = new();
        private readonly Dictionary<(Type, string), bool> _ifaces = new();

        public PropertyInfo? Prop(Type t, string name)
        {
            var key = (t, name);
            if (_props.TryGetValue(key, out var p)) return p;
            p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            _props[key] = p;
            return p;
        }

        public bool Implements(Type t, string interfaceName)
        {
            var key = (t, interfaceName);
            if (_ifaces.TryGetValue(key, out var b)) return b;
            b = t.GetInterfaces().Any(i => i.Name == interfaceName)
             || t.Name == interfaceName;
            _ifaces[key] = b;
            return b;
        }
    }

    private static LiveNode? ExtractNode(object desc, List<(string Id, string Version, string Path)> packages)
    {
        var t = desc.GetType();
        var name = t.GetProperty("Name")?.GetValue(desc)?.ToString();
        if (string.IsNullOrEmpty(name)) return null;
        var category = t.GetProperty("Category")?.GetValue(desc)?.ToString() ?? "";
        var filePath = t.GetProperty("FilePath")?.GetValue(desc)?.ToString() ?? "";
        var fragmented = t.GetProperty("Fragmented")?.GetValue(desc) as bool? ?? false;

        // Check visibility/hidden flags — the node browser hides nodes that have
        // VL-idiomatic replacements (e.g. RotationX from .NET Matrix is hidden because
        // VL provides Rotation with Pitch/Yaw/Roll). Try multiple possible property names.
        var isHidden = t.GetProperty("IsHidden")?.GetValue(desc) as bool? ?? false;
        if (!isHidden)
        {
            var visibility = t.GetProperty("Visibility")?.GetValue(desc)?.ToString() ?? "";
            isHidden = visibility.Contains("Hidden", StringComparison.OrdinalIgnoreCase)
                    || visibility.Contains("Optional", StringComparison.OrdinalIgnoreCase);
        }
        if (!isHidden)
        {
            var flags = t.GetProperty("Flags")?.GetValue(desc)?.ToString() ?? "";
            isHidden = flags.Contains("Hidden", StringComparison.OrdinalIgnoreCase)
                    || flags.Contains("Internal", StringComparison.OrdinalIgnoreCase);
        }
        // Skip fully hidden nodes (not in the node browser at all)
        if (isHidden) return null;

        var inputs = ExtractPins(t.GetProperty("Inputs")?.GetValue(desc) as IEnumerable);
        var outputs = ExtractPins(t.GetProperty("Outputs")?.GetValue(desc) as IEnumerable);

        // Stateful process nodes always carry the hidden "Node Context" input pin.
        var isProcess = inputs.Any(p => p.Name == "Node Context") || fragmented;
        // Filter the infrastructure pin from the public listing
        var publicInputs = inputs.Where(p => p.Name != "Node Context").ToList();

        var package = ResolvePackage(filePath, packages);

        return new LiveNode
        {
            Name = name,
            Category = category,
            FullName = string.IsNullOrEmpty(category) ? name : category + "." + name,
            Kind = isProcess ? "Process" : "Operation",
            Package = package,
            SourceFile = filePath,
            Inputs = publicInputs,
            Outputs = outputs
        };
    }

    private static List<LivePin> ExtractPins(IEnumerable? pins)
    {
        var result = new List<LivePin>();
        if (pins is null) return result;
        foreach (var pin in pins)
        {
            try
            {
                var pt = pin.GetType();
                var pname = pt.GetProperty("Name")?.GetValue(pin)?.ToString();
                if (string.IsNullOrEmpty(pname)) continue;
                var ptype = pt.GetProperty("Type")?.GetValue(pin) as Type;
                var defVal = pt.GetProperty("DefaultValue")?.GetValue(pin);
                var group = pt.GetProperty("PinGroupKind")?.GetValue(pin)?.ToString();

                result.Add(new LivePin
                {
                    Name = pname,
                    Type = PrettyType(ptype),
                    DefaultValue = FormatDefault(defVal),
                    IsPinGroup = group is not null && group != "None" && group != "0"
                });
            }
            catch { }
        }
        return result;
    }

    private static string ResolvePackage(string filePath, List<(string Id, string Version, string Path)> packages)
    {
        if (string.IsNullOrEmpty(filePath)) return "";
        // Longest PackagePath prefix wins (handles nested/extension packages)
        var best = "";
        var bestLen = -1;
        foreach (var (id, _, path) in packages)
        {
            if (path.Length > bestLen &&
                filePath.StartsWith(path, StringComparison.OrdinalIgnoreCase))
            {
                best = id;
                bestLen = path.Length;
            }
        }
        return best;
    }

    internal static string PrettyType(Type? t)
    {
        if (t is null) return "";
        if (t.IsByRef) t = t.GetElementType()!;
        if (t.IsArray) return PrettyType(t.GetElementType()) + "[]";
        if (t.IsGenericType)
        {
            var tn = t.Name;
            var tick = tn.IndexOf('`');
            if (tick > 0) tn = tn.Substring(0, tick);
            return tn + "<" + string.Join(", ", t.GetGenericArguments().Select(PrettyType)) + ">";
        }
        return t.Name switch
        {
            "Single" => "Float32",
            "Double" => "Float64",
            "Int32" => "Integer32",
            "Int64" => "Integer64",
            "UInt32" => "UInteger32",
            "Boolean" => "Boolean",
            "String" => "String",
            "Byte" => "Byte",
            "Char" => "Char",
            "Object" => "Object",
            _ => t.Name
        };
    }

    private static string FormatDefault(object? value)
    {
        if (value is null) return "";
        try
        {
            var s = value switch
            {
                float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
                double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => value.ToString() ?? ""
            };
            return s.Length > 60 ? s.Substring(0, 60) : s;
        }
        catch { return ""; }
    }

    private static object? GetSession()
    {
        var vlLangAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "VL.Lang");
        var sessionType = vlLangAsm?.GetType("VL.Model.VLSession");
        return sessionType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
    }

    // ── Query API (called from HTTP handler threads) ──────────────────────────

    public object Search(string? query, string? category, int limit, bool includePins, bool includeHidden = false)
    {
        List<LiveNode> snapshot;
        lock (_gate) snapshot = _nodes;

        IEnumerable<LiveNode> q = snapshot;

        // Visibility filtering based on vvvv's 4-level node browser:
        //   default mode: only "default" visibility nodes (hi-level, what most users need)
        //   includeHidden: also includes "advanced", "experimental", "obsolete"
        //   "internal" nodes are NEVER returned (not placeable)
        q = q.Where(n => n.Visibility != "internal");
        if (!includeHidden)
            q = q.Where(n => n.Visibility == "default");

        if (!string.IsNullOrWhiteSpace(category))
            q = q.Where(n => n.Category.StartsWith(category, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query))
        {
            var terms = query.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var scored = new List<(LiveNode Node, double Score)>();
            foreach (var n in q)
            {
                var nameL = n.Name.ToLowerInvariant();
                var fullL = n.FullName.ToLowerInvariant();
                var catL = n.Category.ToLowerInvariant();
                double score = 0;
                foreach (var term in terms)
                {
                    if (nameL == term) score += 100;
                    else if (nameL.StartsWith(term)) score += 50;
                    else if (nameL.Contains(term)) score += 25;
                    if (fullL.Contains(term)) score += 15;
                    if (catL.Contains(term)) score += 8;
                }
                // Demote synthesized property/field accessors — real nodes win.
                if (n.IsAccessor) score *= 0.3;
                if (score > 0) scored.Add((n, score));
            }
            q = scored.OrderByDescending(s => s.Score).Select(s => s.Node);
        }
        else
        {
            q = q.OrderBy(n => n.FullName);
        }

        var total = q.Count();
        var page = q.Take(Math.Clamp(limit, 1, 200)).ToList();

        return new
        {
            total,
            count = page.Count,
            builtAt = _builtAt,
            nodes = page.Select(n => SerializeNode(n, includePins))
        };
    }

    public object Lookup(string name, string? category, bool includeHidden = false)
    {
        List<LiveNode> snapshot;
        lock (_gate) snapshot = _nodes;

        // Filter by visibility: exclude internal always, exclude non-default unless requested
        var pool = snapshot.Where(n => n.Visibility != "internal").ToList();
        if (!includeHidden)
            pool = pool.Where(n => n.Visibility == "default").ToList();

        var matches = pool
            .Where(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
        {
            // Try full name ("Stride.Models.Box") and suffix match
            matches = pool
                .Where(n => n.FullName.Equals(name, StringComparison.OrdinalIgnoreCase)
                         || n.FullName.EndsWith("." + name, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        if (!string.IsNullOrWhiteSpace(category))
        {
            var filtered = matches
                .Where(m => m.Category.StartsWith(category, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (filtered.Count > 0) matches = filtered;
        }

        if (matches.Count == 0)
        {
            var suggestions = snapshot
                .Where(n => n.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                .Take(8)
                .Select(n => n.FullName)
                .ToList();
            return new { found = false, suggestions };
        }

        return new
        {
            found = true,
            matchCount = matches.Count,
            nodes = matches.Take(5).Select(n => SerializeNode(n, includePins: true))
        };
    }

    public object GetCategories(string? prefix)
    {
        List<LiveNode> snapshot;
        lock (_gate) snapshot = _nodes;
        var cats = snapshot
            .Select(n => n.Category)
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(prefix))
            cats = cats.Where(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        var list = cats.OrderBy(c => c).ToList();
        return new { count = list.Count, categories = list };
    }

    private static object SerializeNode(LiveNode n, bool includePins)
    {
        if (!includePins)
        {
            return new
            {
                n.Name,
                n.Category,
                n.FullName,
                n.Kind,
                n.Package,
                accessor = n.IsAccessor ? true : (bool?)null,
                visibility = n.Visibility != "default" ? n.Visibility : null,
                inputCount = n.Inputs.Count,
                outputCount = n.Outputs.Count
            };
        }
        return new
        {
            n.Name,
            n.Category,
            n.FullName,
            n.Kind,
            n.Package,
            n.SourceFile,
            n.IsGeneric,
            accessor = n.IsAccessor ? true : (bool?)null,
            visibility = n.Visibility != "default" ? n.Visibility : null,
            inputs = n.Inputs.Select(p => new { p.Name, p.Type, p.DefaultValue, p.IsPinGroup, hidden = p.IsHidden ? true : (bool?)null, optional = p.IsOptional ? true : (bool?)null, state = p.IsState ? true : (bool?)null }),
            outputs = n.Outputs.Select(p => new { p.Name, p.Type, p.IsPinGroup, hidden = p.IsHidden ? true : (bool?)null, optional = p.IsOptional ? true : (bool?)null, state = p.IsState ? true : (bool?)null })
        };
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    internal class FactoryStat
    {
        public string Factory { get; set; } = "?";
        public int Nodes { get; set; }
        public string? Note { get; set; }
        public string? Error { get; set; }
    }

    internal class LiveNode
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Kind { get; set; } = "Operation";
        public string Package { get; set; } = "";
        public string SourceFile { get; set; } = "";
        public bool IsGeneric { get; set; }
        /// <summary>True for synthesized property/field accessor nodes (demoted in search).</summary>
        public bool IsAccessor { get; set; }
        /// <summary>
        /// Node browser visibility level matching vvvv's 4-level system:
        ///   "default"      — hi-level nodes (shown in default node browser)
        ///   "advanced"     — low-level/advanced (hidden by default, shown with "show advanced")
        ///   "experimental" — future/unstable (shown with experimental badge)
        ///   "obsolete"     — deprecated (shown with strikethrough)
        ///   "internal"     — completely hidden (never shown, not placeable)
        /// </summary>
        public string Visibility { get; set; } = "default";
        /// <summary>Raw smell string from the VL symbol (for diagnostics).</summary>
        public string Smell { get; set; } = "";
        public List<LivePin> Inputs { get; set; } = new();
        public List<LivePin> Outputs { get; set; } = new();
    }

    internal class LivePin
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string DefaultValue { get; set; } = "";
        public bool IsPinGroup { get; set; }
        public bool IsHidden { get; set; }
        public bool IsOptional { get; set; }
        public bool IsState { get; set; }
    }
}
