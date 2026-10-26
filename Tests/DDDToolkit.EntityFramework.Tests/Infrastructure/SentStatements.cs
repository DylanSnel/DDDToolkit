using Microsoft.Extensions.Logging;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// Every command Npgsql sent over the connections of one data source, as Npgsql itself logs them: what reached
/// the server, whoever sent it, the interceptor's own statements included, which no Entity Framework
/// interceptor sees.
/// </summary>
public sealed class SentStatements : ILoggerProvider
{
    private const string Commands = "Npgsql.Command";

    private readonly List<string> _sent = [];
    private readonly ILoggerFactory _factory;

    public SentStatements() => _factory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Debug).AddProvider(this));

    /// <summary>What was sent so far, one entry per round trip, in order: the text of a command, or of every statement of a batch.</summary>
    public IReadOnlyList<string> Sent
    {
        get
        {
            lock (_sent)
            {
                return [.. _sent];
            }
        }
    }

    /// <summary>A data source on <paramref name="connectionString"/> whose commands are kept here.</summary>
    public NpgsqlDataSource DataSource(string connectionString)
        => new NpgsqlDataSourceBuilder(connectionString).UseLoggerFactory(_factory).Build();

    /// <summary>How many round trips carried <paramref name="text"/>.</summary>
    public int Count(string text) => Sent.Count(sent => sent.Contains(text, StringComparison.Ordinal));

    public ILogger CreateLogger(string categoryName) => new Keeper(this, categoryName);

    public void Dispose() => _factory.Dispose();

    private sealed class Keeper(SentStatements statements, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => category == Commands;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // One entry per command or batch, written when it completed or failed; nothing else Npgsql says counts.
            var message = formatter(state, exception);
            if (category != Commands || !(message.Contains("execution completed", StringComparison.Ordinal) || message.Contains("execution failed", StringComparison.Ordinal)))
            {
                return;
            }

            lock (statements._sent)
            {
                statements._sent.Add(message);
            }
        }
    }
}
