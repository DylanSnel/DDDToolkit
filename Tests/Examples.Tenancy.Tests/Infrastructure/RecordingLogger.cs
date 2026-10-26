using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>A log entry: its level, the category that wrote it, and the message.</summary>
public sealed record LogEntry(LogLevel Level, string Category, string Message);

/// <summary>A logger provider that keeps what every logger of the host writes, for a test to read back.</summary>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    /// <summary>Everything logged so far.</summary>
    public IReadOnlyList<LogEntry> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName) => new Recorder(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class Recorder(string category, ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                entries.Enqueue(new LogEntry(logLevel, category, formatter(state, exception)));
            }
        }
    }
}
