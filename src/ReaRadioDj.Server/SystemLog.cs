namespace ReaRadioDj.Server;

public sealed record SystemLogEntry(long Id, DateTime Time, string Level, string Source, string Text);

/// <summary>
/// The engine's own log lines (warnings and errors always, information when "uitgebreid" is on), kept in memory so the
/// studio can show them without anyone looking in files. The framework's information lines stay out.
/// </summary>
public sealed class SystemLog : ILoggerProvider
{
    public static SystemLog Instance { get; } = new();

    private const int Size = 2000;
    private readonly object _gate = new();
    private readonly Queue<SystemLogEntry> _lines = new();
    private long _next;

    /// <summary>When true, information lines of the engine are kept as well.</summary>
    public bool Detailed { get; set; }

    public ILogger CreateLogger(string categoryName) => new Writer(this, categoryName);

    public void Dispose() { }

    public IReadOnlyList<SystemLogEntry> Since(long id, bool detailed)
    {
        lock (_gate)
            return _lines.Where(l => l.Id > id && (detailed || l.Level is not "info")).ToList();
    }

    private void Add(LogLevel level, string category, string text)
    {
        var source = category.Split('.').Last();
        var name = level switch { LogLevel.Critical or LogLevel.Error => "fout", LogLevel.Warning => "waarschuwing", _ => "info" };
        lock (_gate)
        {
            _lines.Enqueue(new SystemLogEntry(++_next, DateTime.Now, name, source, text));
            while (_lines.Count > Size) _lines.Dequeue();
        }
    }

    private sealed class Writer(SystemLog log, string category) : ILogger
    {
        private readonly bool _framework = category.StartsWith("Microsoft.", StringComparison.Ordinal) || category.StartsWith("System.", StringComparison.Ordinal);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel level) =>
            level >= LogLevel.Warning || (level == LogLevel.Information && log.Detailed && !_framework);

        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            var text = formatter(state, exception);
            if (exception is not null) text += $" ({exception.GetType().Name}: {exception.Message})";
            log.Add(level, category, text);
        }
    }
}
