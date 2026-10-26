using System.Collections.Concurrent;

namespace DDDToolkit.HotChocolate.Fusion.InMemory.Tests.Infrastructure;

/// <summary>
/// Everything an application logs, at every level, as text: each entry's category, its message, the values of its
/// state and its exception. For a test that a secret is never among them.
/// </summary>
internal sealed class LogRecorder : ILoggerProvider
{
    private readonly ConcurrentQueue<Entry> _entries = new();

    /// <summary>What was logged, in order.</summary>
    public IReadOnlyList<Entry> Entries => [.. _entries];

    /// <summary>Every entry as one text: whatever was logged is somewhere in it.</summary>
    public string Everything => string.Join(Environment.NewLine, _entries.Select(entry => entry.ToString()));

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

    public void Dispose()
    {
    }

    /// <summary>One entry, with everything it carries as text.</summary>
    public sealed record Entry(string Category, LogLevel Level, string Message, string State, string? Exception)
    {
        public override string ToString() => $"{Level} {Category}: {Message} | {State} | {Exception}";
    }

    private sealed class Logger(string category, ConcurrentQueue<Entry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join(", ", pairs.Select(pair => $"{pair.Key}={pair.Value}"))
                : state?.ToString() ?? string.Empty;

            entries.Enqueue(new Entry(category, logLevel, formatter(state, exception), values, exception?.ToString()));
        }
    }
}
