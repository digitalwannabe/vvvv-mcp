using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace VL.MCP;

/// <summary>
/// Custom ILoggerProvider that captures log entries in a ring buffer.
/// Register with vvvv's LoggerFactory to capture ILogger output.
/// Also used by ConsoleTee to capture System.Console output.
/// </summary>
internal class BridgeLogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private const int MaxEntries = 500;

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);
    public void Dispose() { }

    internal void AddEntry(LogEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > MaxEntries)
            _entries.TryDequeue(out _);
    }

    public List<LogEntry> GetEntries(int limit = 50, string? minSeverity = null)
    {
        var all = _entries.ToArray();
        IEnumerable<LogEntry> filtered = all;
        if (minSeverity is not null)
        {
            var minLevel = minSeverity.ToLowerInvariant() switch
            {
                "error" or "critical"       => LogLevel.Error,
                "warning"                   => LogLevel.Warning,
                "info" or "information"     => LogLevel.Information,
                _                           => LogLevel.Trace
            };
            filtered = all.Where(e => e.Level >= minLevel);
        }
        return filtered.TakeLast(limit).ToList();
    }

    public void Clear() => _entries.Clear();

    private class CaptureLogger : ILogger
    {
        private readonly BridgeLogCapture _capture;
        private readonly string _category;
        public CaptureLogger(BridgeLogCapture capture, string category) { _capture = capture; _category = category; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            _capture.AddEntry(new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level     = logLevel,
                Category  = _category,
                Message   = formatter(state, exception),
                Exception = exception?.Message
            });
        }
    }
}

/// <summary>
/// Tees Console.Out / Console.Error into BridgeLogCapture so that all
/// System.Console output (including [OpenWebUI] and vvvv Sys entries)
/// is readable via get_vvvv_log through the MCP.
/// Writes are forwarded to the original stream so vvvv's console panel still shows them.
/// </summary>
internal sealed class ConsoleTee : TextWriter
{
    private readonly TextWriter       _original;
    private readonly BridgeLogCapture _capture;
    private readonly LogLevel         _level;
    private readonly string           _category;

    public override System.Text.Encoding Encoding => _original.Encoding;

    public ConsoleTee(TextWriter original, BridgeLogCapture capture, LogLevel level, string category)
    {
        _original = original;
        _capture  = capture;
        _level    = level;
        _category = category;
    }

    public override void WriteLine(string? value)
    {
        _original.WriteLine(value);
        if (!string.IsNullOrWhiteSpace(value))
            _capture.AddEntry(new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level     = _level,
                Category  = _category,
                Message   = value
            });
    }

    // Pass-through for char/string writes (no buffering — vvvv handles display)
    public override void Write(char value)   => _original.Write(value);
    public override void Write(string? value) => _original.Write(value);
    public override void Flush()             => _original.Flush();
    protected override void Dispose(bool disposing) { if (disposing) _original.Dispose(); }
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public LogLevel Level     { get; set; }
    public string   Category  { get; set; } = "";
    public string   Message   { get; set; } = "";
    public string?  Exception { get; set; }
    public string   Severity  => Level.ToString();
}
