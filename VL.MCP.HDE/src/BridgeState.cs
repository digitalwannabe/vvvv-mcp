using System.Collections;
using System.Reflection;
using VL.Core;

namespace VL.MCP;

/// <summary>
/// Holds a snapshot of the current vvvv editor state, updated each frame.
/// Uses reflection to access VL.Lang/VL.Model APIs (only available at runtime inside vvvv).
/// </summary>
internal class BridgeState
{
    public DateTime StartTime { get; } = DateTime.UtcNow;
    public long FrameCount { get; set; }
    public float UptimeSeconds { get; set; }
    public bool IsRunning { get; set; } = true;
    public bool IsPaused { get; set; }

    public List<DocumentInfo> Documents { get; set; } = new();
    public List<ErrorInfo> Errors { get; set; } = new();
    public List<PackageInfo> Packages { get; set; } = new();
    public List<ChannelInfo> Channels { get; set; } = new();

    // Reflection cache
    private object? _session;
    private bool _initialized;
    private bool _failed;

    // Cached property accessors
    private PropertyInfo? _currentSolutionProp;
    private PropertyInfo? _docsProp;
    private PropertyInfo? _messageChannelProp;
    private PropertyInfo? _messageChannelValueProp;
    private PropertyInfo? _userRuntimeProp;
    private PropertyInfo? _runtimeMessagesProp;
    private PropertyInfo? _runtimeModeProp;
    private PropertyInfo? _runtimeFrameProp;
    private PropertyInfo? _availableNugetsProp;

    private void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            var vlLangAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "VL.Lang");
            if (vlLangAsm is null) { _failed = true; return; }

            var sessionType = vlLangAsm.GetType("VL.Model.VLSession");
            if (sessionType is null) { _failed = true; return; }

            var instanceProp = sessionType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            _session = instanceProp?.GetValue(null);
            if (_session is null) { _failed = true; return; }

            var sType = _session.GetType();

            // Cache property lookups
            _currentSolutionProp = sType.GetProperty("CurrentSolution");
            _messageChannelProp = sType.GetProperty("LatestMessagesFromCompiler");
            _userRuntimeProp = sType.GetProperty("UserRuntime");
            _availableNugetsProp = sType.GetProperty("AvailableNugets");
        }
        catch
        {
            _failed = true;
        }
    }

    public void UpdateDocuments(AppHost appHost)
    {
        Initialize();
        if (_failed || _session is null) return;

        var docs = new List<DocumentInfo>();
        try
        {
            var solution = _currentSolutionProp?.GetValue(_session);
            if (solution is null) return;

            // Cache the Documents property on Solution
            _docsProp ??= solution.GetType().GetProperty("Documents");
            var docsEnum = _docsProp?.GetValue(solution) as IEnumerable;
            if (docsEnum is null) return;

            foreach (var doc in docsEnum)
            {
                var docType = doc.GetType();
                var filePath = docType.GetProperty("FilePath")?.GetValue(doc)?.ToString();
                var name = docType.GetProperty("Name")?.GetValue(doc)?.ToString()
                        ?? Path.GetFileName(filePath ?? "unknown");
                var isSaved = docType.GetProperty("IsSaved")?.GetValue(doc) as bool? ?? true;
                var isChanged = docType.GetProperty("IsChanged")?.GetValue(doc) as bool? ?? false;
                var isReadOnly = docType.GetProperty("IsReadOnly")?.GetValue(doc) as bool? ?? false;

                if (filePath is not null)
                {
                    docs.Add(new DocumentInfo
                    {
                        Name = name,
                        FilePath = filePath,
                        IsSaved = isSaved,
                        IsChanged = isChanged,
                        IsReadOnly = isReadOnly
                    });
                }
            }
        }
        catch { }

        Documents = docs;
    }

    public void UpdateErrors(AppHost appHost)
    {
        Initialize();
        if (_failed || _session is null) return;

        var errors = new List<ErrorInfo>();
        try
        {
            // Compilation messages (red nodes / build errors)
            var channel = _messageChannelProp?.GetValue(_session);
            if (channel is not null)
            {
                _messageChannelValueProp ??= channel.GetType().GetProperty("Value");
                var messagesSet = _messageChannelValueProp?.GetValue(channel) as IEnumerable;
                if (messagesSet is not null)
                {
                    foreach (var msg in messagesSet)
                    {
                        var err = ExtractMessage(msg, "Compile");
                        if (err is not null) errors.Add(err);
                    }
                }
            }

            // Runtime messages (pink nodes / exceptions)
            var runtime = _userRuntimeProp?.GetValue(_session);
            if (runtime is not null)
            {
                _runtimeMessagesProp ??= runtime.GetType().GetProperty("RuntimeMessages");
                var runtimeMsgs = _runtimeMessagesProp?.GetValue(runtime) as IEnumerable;
                if (runtimeMsgs is not null)
                {
                    foreach (var msg in runtimeMsgs)
                    {
                        var err = ExtractMessage(msg, "Runtime");
                        if (err is not null) errors.Add(err);
                    }
                }
            }
        }
        catch { }

        Errors = errors;
    }

    private static ErrorInfo? ExtractMessage(object msg, string source)
    {
        try
        {
            var msgType = msg.GetType();
            var text = msgType.GetProperty("What")?.GetValue(msg)?.ToString()
                    ?? msgType.GetProperty("Message")?.GetValue(msg)?.ToString()
                    ?? msgType.GetProperty("Text")?.GetValue(msg)?.ToString()
                    ?? msg.ToString();
            var severity = msgType.GetProperty("Severity")?.GetValue(msg)?.ToString()
                        ?? msgType.GetProperty("Kind")?.GetValue(msg)?.ToString()
                        ?? "Error";
            var location = msgType.GetProperty("Location")?.GetValue(msg)?.ToString()
                        ?? msgType.GetProperty("Where")?.GetValue(msg)?.ToString();

            return new ErrorInfo
            {
                Message = text ?? "",
                Severity = severity,
                Location = location,
                Source = source
            };
        }
        catch { return null; }
    }

    public void UpdateRunningState(AppHost appHost)
    {
        Initialize();
        if (_failed || _session is null) return;

        try
        {
            var runtime = _userRuntimeProp?.GetValue(_session);
            if (runtime is not null)
            {
                _runtimeModeProp ??= runtime.GetType().GetProperty("Mode");
                _runtimeFrameProp ??= runtime.GetType().GetProperty("Frame");

                var mode = _runtimeModeProp?.GetValue(runtime)?.ToString();
                IsRunning = mode == "Running";
                IsPaused = mode == "Paused";

                if (_runtimeFrameProp?.GetValue(runtime) is ulong frame)
                {
                    FrameCount = (long)frame;
                }
            }
        }
        catch { }
    }

    public void UpdatePackages()
    {
        Initialize();
        if (_failed || _session is null) return;

        // Only update packages occasionally (they don't change often)
        if (Packages.Count > 0 && FrameCount % 300 != 0) return;

        var packages = new List<PackageInfo>();
        try
        {
            var nugets = _availableNugetsProp?.GetValue(_session) as IEnumerable;
            if (nugets is null) return;

            foreach (var pkg in nugets)
            {
                var pkgType = pkg.GetType();
                var id = pkgType.GetProperty("Id")?.GetValue(pkg)?.ToString();
                var version = pkgType.GetProperty("Version")?.GetValue(pkg)?.ToString();
                var isVL = pkgType.GetProperty("IsVLPackage")?.GetValue(pkg) as bool? ?? false;
                var isSource = pkgType.GetProperty("IsSourcePackage")?.GetValue(pkg) as bool? ?? false;
                var isHDE = pkgType.GetProperty("IsHDEPackage")?.GetValue(pkg) as bool? ?? false;

                if (id is not null && isVL)
                {
                    packages.Add(new PackageInfo
                    {
                        Name = id,
                        Version = version,
                        Source = isSource ? "source" : "binary",
                        IsExtension = isHDE
                    });
                }
            }
        }
        catch { }

        Packages = packages;
    }
}

// ── Response DTOs ──────────────────────────────────────────────────────────────

/// <summary>Info about an open document in vvvv.</summary>
public class DocumentInfo
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public bool IsActive { get; set; }
    public bool IsSaved { get; set; } = true;
    public bool IsChanged { get; set; }
    public bool IsReadOnly { get; set; }
}

/// <summary>A compilation or runtime error/warning.</summary>
public class ErrorInfo
{
    public string Message { get; set; } = "";
    public string? Severity { get; set; }
    public string? Location { get; set; }
    public string? Source { get; set; }
}

/// <summary>An installed VL package.</summary>
public class PackageInfo
{
    public string Name { get; set; } = "";
    public string? Version { get; set; }
    public string? Source { get; set; }
    public bool IsExtension { get; set; }
}

/// <summary>A public channel exposed in the running patch.</summary>
public class ChannelInfo
{
    public string Name { get; set; } = "";
    public string? Type { get; set; }
    public string? Value { get; set; }
    public string? Direction { get; set; }
}
