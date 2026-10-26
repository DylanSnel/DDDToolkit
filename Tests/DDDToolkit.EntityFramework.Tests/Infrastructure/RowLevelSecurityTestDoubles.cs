using DDDToolkit.Abstractions.Access;
using DDDToolkit.EntityFramework.Postgres;
using Microsoft.Extensions.Logging;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>A logger that keeps what it was told, for the tests that ask whether a warning was written.</summary>
public sealed class KeptLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_entries)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}

/// <summary>Every logger of a host, keeping the exceptions they were told about.</summary>
public sealed class KeptLogs : ILoggerProvider
{
    private readonly List<Exception> _exceptions = [];

    public IReadOnlyList<Exception> Exceptions
    {
        get
        {
            lock (_exceptions)
            {
                return [.. _exceptions];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new Keeper(this);

    public void Dispose()
    {
    }

    private sealed class Keeper(KeptLogs logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is not null)
            {
                lock (logs._exceptions)
                {
                    logs._exceptions.Add(exception);
                }
            }
        }
    }
}

/// <summary>
/// A module's settings as the tests want them: it owns <c>test.tenant</c> and <c>test.unit</c>, and gives the
/// tenant the test set, or nothing.
/// </summary>
public sealed class TenantSetting : IRowLevelSecuritySettings
{
    public const string Tenant = "test.tenant";

    public const string Unit = "test.unit";

    /// <summary>The tenant the next connection gets; <see langword="null"/> leaves the setting out.</summary>
    public string? Current { get; set; }

    public IReadOnlyCollection<string> Names { get; } = [Tenant, Unit];

    public IEnumerable<KeyValuePair<string, string>> For(Caller caller)
        => Current is { } tenant ? [new(Tenant, tenant)] : [];
}

/// <summary>A provider that declares what it is given and answers with whatever it is given, right or wrong.</summary>
public sealed class DeclaredSettings(IReadOnlyCollection<string> names, params KeyValuePair<string, string>[] values) : IRowLevelSecuritySettings
{
    public IReadOnlyCollection<string> Names { get; } = names;

    public IEnumerable<KeyValuePair<string, string>> For(Caller caller) => values;
}
