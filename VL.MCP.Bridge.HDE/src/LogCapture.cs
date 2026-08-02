using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace VL.MCP.Bridge;

/// <summary>
/// Custom ILoggerProvider that captures log entries in a ring buffer.
/// Register this with vvvv's LoggerFactory to capture console output.
/// </summary>
internal class BridgeLogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private const int MaxEntries = 200;

    public ILogger CreateLogger(string categoryName)
    {
        return new CaptureLogger(this, categoryName);
    }

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
                "error" or "critical" => LogLevel.Error,
                "warning" => LogLevel.Warning,
                "info" or "information" => LogLevel.Information,
                "debug" => LogLevel.Debug,
                _ => LogLevel.Trace
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

        public CaptureLogger(BridgeLogCapture capture, string category)
        {
            _capture = capture;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            _capture.AddEntry(new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = logLevel,
                Category = _category,
                Message = formatter(state, exception),
                Exception = exception?.Message
            });
        }
    }
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Exception { get; set; }
    public string Severity => Level.ToString();
}
