using System.Reflection;
using VL.Core;

namespace VL.MCP.Bridge;

/// <summary>
/// Holds a snapshot of the current vvvv editor state, updated each frame.
/// Uses reflection-based access to VL.Lang APIs since those types are only
/// loaded at runtime inside the vvvv editor (not compile-time dependencies).
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

    // Cache reflection lookups
    private Type? _sessionType;
    private object? _session;
    private bool _reflectionInitialized;
    private bool _reflectionFailed;

    /// <summary>
    /// Try to get the VL session via reflection.
    /// VL.Lang is loaded at runtime in the vvvv process, not at compile time.
    /// </summary>
    private object? GetSession(AppHost appHost)
    {
        if (_reflectionFailed) return null;

        if (!_reflectionInitialized)
        {
            _reflectionInitialized = true;
            try
            {
                // Strategy 1: Look for VL.Lang assembly loaded in the process
                var vlLangAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "VL.Lang");

                if (vlLangAssembly is not null)
                {
                    _sessionType = vlLangAssembly.GetType("VL.Lang.VLSession")
                                ?? vlLangAssembly.GetType("VL.Lang.Session");

                    if (_sessionType is not null)
                    {
                        // Try to resolve from ServiceRegistry
                        _session = appHost.Services.GetService(_sessionType);
                    }
                }

                // Strategy 2: Look for a Session property on AppHost via reflection
                if (_session is null)
                {
                    var sessionProp = appHost.GetType().GetProperty("Session",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (sessionProp is not null)
                    {
                        _session = sessionProp.GetValue(appHost);
                        _sessionType = _session?.GetType();
                    }
                }

                if (_session is null)
                {
                    _reflectionFailed = true;
                }
            }
            catch
            {
                _reflectionFailed = true;
            }
        }

        return _session;
    }

    public void UpdateDocuments(AppHost appHost)
    {
        var docs = new List<DocumentInfo>();

        try
        {
            var session = GetSession(appHost);
            if (session is null) return;

            // Try: session.CurrentSolution.Documents
            var solutionProp = _sessionType?.GetProperty("CurrentSolution",
                BindingFlags.Public | BindingFlags.Instance);
            var solution = solutionProp?.GetValue(session);
            if (solution is null) return;

            var docsProp = solution.GetType().GetProperty("Documents",
                BindingFlags.Public | BindingFlags.Instance);
            var docsEnumerable = docsProp?.GetValue(solution) as System.Collections.IEnumerable;
            if (docsEnumerable is null) return;

            foreach (var doc in docsEnumerable)
            {
                var docType = doc.GetType();
                var filePath = docType.GetProperty("FilePath")?.GetValue(doc)?.ToString()
                            ?? docType.GetProperty("Path")?.GetValue(doc)?.ToString();
                var name = docType.GetProperty("Name")?.GetValue(doc)?.ToString()
                        ?? Path.GetFileName(filePath ?? "unknown");
                var isActive = docType.GetProperty("IsActive")?.GetValue(doc) as bool? ?? false;

                if (filePath is not null)
                {
                    docs.Add(new DocumentInfo
                    {
                        Name = name,
                        FilePath = filePath,
                        IsActive = isActive
                    });
                }
            }
        }
        catch { /* State collection should never crash */ }

        Documents = docs;
    }

    public void UpdateErrors(AppHost appHost)
    {
        var errors = new List<ErrorInfo>();

        try
        {
            var session = GetSession(appHost);
            if (session is null) return;

            var solutionProp = _sessionType?.GetProperty("CurrentSolution",
                BindingFlags.Public | BindingFlags.Instance);
            var solution = solutionProp?.GetValue(session);
            if (solution is null) return;

            // Look for Messages, Errors, or Diagnostics property
            var messagesProp = solution.GetType().GetProperty("Messages",
                BindingFlags.Public | BindingFlags.Instance)
                ?? solution.GetType().GetProperty("Errors",
                BindingFlags.Public | BindingFlags.Instance)
                ?? solution.GetType().GetProperty("Diagnostics",
                BindingFlags.Public | BindingFlags.Instance);

            var messagesEnumerable = messagesProp?.GetValue(solution) as System.Collections.IEnumerable;
            if (messagesEnumerable is null) return;

            foreach (var msg in messagesEnumerable)
            {
                var msgType = msg.GetType();
                var text = msgType.GetProperty("Message")?.GetValue(msg)?.ToString()
                        ?? msgType.GetProperty("Text")?.GetValue(msg)?.ToString()
                        ?? msg.ToString();
                var severity = msgType.GetProperty("Severity")?.GetValue(msg)?.ToString()
                            ?? msgType.GetProperty("Kind")?.GetValue(msg)?.ToString()
                            ?? "Error";
                var location = msgType.GetProperty("Location")?.GetValue(msg)?.ToString()
                            ?? msgType.GetProperty("FilePath")?.GetValue(msg)?.ToString();

                errors.Add(new ErrorInfo
                {
                    Message = text ?? "",
                    Severity = severity,
                    Location = location
                });
            }
        }
        catch { /* State collection should never crash */ }

        Errors = errors;
    }

    public void UpdateRunningState(AppHost appHost)
    {
        try
        {
            var runtimeProp = appHost.GetType().GetProperty("Runtime",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (runtimeProp is not null)
            {
                var runtime = runtimeProp.GetValue(appHost);
                if (runtime is not null)
                {
                    var isPausedProp = runtime.GetType().GetProperty("IsPaused",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (isPausedProp is not null)
                        IsPaused = isPausedProp.GetValue(runtime) as bool? ?? false;

                    var isRunningProp = runtime.GetType().GetProperty("IsRunning",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (isRunningProp is not null)
                        IsRunning = isRunningProp.GetValue(runtime) as bool? ?? true;
                }
            }
        }
        catch { /* State collection should never crash */ }
    }
}

// ── Response DTOs ──────────────────────────────────────────────────────────────

/// <summary>Info about an open document in vvvv.</summary>
public class DocumentInfo
{
    /// <summary>Document display name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Absolute path to the .vl file.</summary>
    public string FilePath { get; set; } = "";
    /// <summary>Whether this is the currently active/focused document.</summary>
    public bool IsActive { get; set; }
}

/// <summary>A compilation error or warning.</summary>
public class ErrorInfo
{
    /// <summary>Error/warning text.</summary>
    public string Message { get; set; } = "";
    /// <summary>Severity level (Error, Warning, Info).</summary>
    public string? Severity { get; set; }
    /// <summary>Source location (file path, node, etc.).</summary>
    public string? Location { get; set; }
}

/// <summary>A referenced NuGet package.</summary>
public class PackageInfo
{
    /// <summary>Package name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Package version.</summary>
    public string? Version { get; set; }
    /// <summary>Source (nuget, local, etc.).</summary>
    public string? Source { get; set; }
}

/// <summary>A public channel exposed in the running patch.</summary>
public class ChannelInfo
{
    /// <summary>Channel name/path.</summary>
    public string Name { get; set; } = "";
    /// <summary>Value type.</summary>
    public string? Type { get; set; }
    /// <summary>Current value as string.</summary>
    public string? Value { get; set; }
    /// <summary>Direction (In, Out, InOut).</summary>
    public string? Direction { get; set; }
}
