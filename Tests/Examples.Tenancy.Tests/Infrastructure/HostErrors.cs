using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// What a host logged as an error, each with the exception it was logged with: the reason behind a 500, which the
/// response itself keeps from its client, and the reason a host did not start.
/// </summary>
/// <remarks>
/// <see cref="SampleFactory"/> adds one to every host. A request runs apart from the test that sent it, so the
/// host cannot write to that test's output itself; the client the factory hands out does, when an answer is a
/// 500 (<see cref="Describe"/>).
/// </remarks>
public sealed class HostErrors : ILoggerProvider
{
    private readonly ConcurrentQueue<(string Category, string Message, Exception? Exception)> _entries = new();

    /// <summary>How many errors were logged so far.</summary>
    public int Count => _entries.Count;

    /// <summary>Every exception an error was logged with, in the order they were logged.</summary>
    public IReadOnlyList<Exception> Thrown => [.. _entries.Select(entry => entry.Exception).OfType<Exception>()];

    /// <summary>The errors logged after the first <paramref name="from"/>, as text: who logged each, what it said and what was thrown.</summary>
    public string Describe(int from = 0)
        => string.Join(Environment.NewLine, _entries.Skip(from).Select(entry => $"{entry.Category}: {entry.Message}{Environment.NewLine}{entry.Exception}"));

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new Recorder(categoryName, _entries);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class Recorder(string category, ConcurrentQueue<(string Category, string Message, Exception? Exception)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel is >= LogLevel.Error and not LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                entries.Enqueue((category, formatter(state, exception), exception));
            }
        }
    }
}
