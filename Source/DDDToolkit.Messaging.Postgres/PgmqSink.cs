using System.Text.Json;
using DDDToolkit.BaseTypes;
using DDDToolkit.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace DDDToolkit.Messaging.Postgres;

/// <summary>
/// Enqueues published messages on a pgmq queue over a connection of its own, from an
/// <see cref="NpgsqlDataSource"/>.
/// <para>
/// Use this one when the queue lives in a database this process does not otherwise write to. When the
/// queue is in the same database as the aggregates, prefer <see cref="PgmqSink{TContext}"/>: it sends on
/// the context's connection, so the enqueue joins whatever transaction that context is in, which is the
/// whole reason to choose pgmq over a broker.
/// </para>
/// </summary>
public sealed class PgmqSink : IIntegrationEventSink
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly PgmqSinkOptions _options;
    private readonly PgmqDispatcher _dispatcher;

    /// <summary>Creates a sink that opens its own connections.</summary>
    /// <param name="dataSource">Where connections come from.</param>
    /// <param name="options">Which queue, and what rides alongside the payload.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dataSource"/> or <paramref name="options"/> is null.</exception>
    public PgmqSink(NpgsqlDataSource dataSource, PgmqSinkOptions options)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _dispatcher = new PgmqDispatcher(options);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    /// <exception cref="PgmqNotInstalledException">The pgmq extension is not installed in that database.</exception>
    public async Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await _dispatcher.SendAsync(
            connection,
            transaction: null,
            message,
            token => _dataSource.OpenConnectionAsync(token),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Enqueues published messages on a pgmq queue over the connection of <typeparamref name="TContext"/>,
/// joining whatever transaction that context currently has.
/// <para>
/// <b>Why this is the interesting one.</b> A pgmq queue is an ordinary Postgres table and
/// <c>pgmq.send</c> is an insert, so the enqueue is transactional like any other write. Put this sink
/// behind the outbox with <c>outbox.DeliverInTransaction = true</c> and the handoff from the outbox row
/// to the queue is one commit: either the message is on the queue and the row is marked processed, or
/// neither happened. No broker can offer that, which is why every broker needs an outbox in front of it.
/// </para>
/// <para>
/// You can also skip the outbox entirely and call
/// <see cref="PgmqQueue.SendAsync(NpgsqlConnection, NpgsqlTransaction, string, string, string, CancellationToken)"/>
/// inside your own transaction, so the aggregate and the message commit together. The outbox is still the
/// better default: it keeps the queue name and the payload shape out of the aggregate's code path, and it
/// retries for you. See <c>docs/integration-events.md</c>.
/// </para>
/// <para>
/// Everything after the commit is ordinary at-least-once queueing: a consumer reads, does its work, and
/// archives the message. Reading hides a message for a visibility timeout rather than removing it, so a
/// consumer that dies mid-work gets the message again, and the inbox is what makes that harmless.
/// </para>
/// </summary>
/// <typeparam name="TContext">The context whose connection and transaction the enqueue joins.</typeparam>
public sealed class PgmqSink<TContext> : IIntegrationEventSink where TContext : DbContext
{
    private readonly TContext _context;
    private readonly PgmqDispatcher _dispatcher;

    /// <summary>Creates a sink over the (scoped) <paramref name="context"/>.</summary>
    /// <param name="context">The context whose connection the message is enqueued on.</param>
    /// <param name="options">Which queue, and what rides alongside the payload.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="options"/> is null.</exception>
    public PgmqSink(TContext context, PgmqSinkOptions options)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        ArgumentNullException.ThrowIfNull(options);
        _dispatcher = new PgmqDispatcher(options);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The context is not on a Postgres connection.</exception>
    /// <exception cref="PgmqNotInstalledException">The pgmq extension is not installed in that database.</exception>
    public async Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_context.Database.GetDbConnection() is not NpgsqlConnection connection)
        {
            throw new InvalidOperationException(
                $"'{typeof(TContext).Name}' is not on a Npgsql connection ({_context.Database.ProviderName ?? "no provider"}), so it cannot reach a pgmq queue. " +
                $"Use the {nameof(PgmqSink)} overload that takes an NpgsqlDataSource when the queue lives in another database.");
        }

        // The context's own transaction, so the enqueue commits with whatever else it is doing.
        var transaction = _context.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;

        var opened = await PgmqQueue.EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        try
        {
            await _dispatcher.SendAsync(connection, transaction, message, OpenSeparatelyAsync, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// A second connection to the same database, for creating the queue. Creating a queue is DDL and
    /// Postgres rolls DDL back like anything else, so doing it on the caller's transaction would make the
    /// queue vanish whenever that transaction rolled back. A queue is deployment state; it does not
    /// belong to the business transaction that happened to notice it was missing.
    /// </summary>
    private async ValueTask<NpgsqlConnection> OpenSeparatelyAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_context.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}

/// <summary>
/// The part both sinks share: name the queue, create it once, build the headers, send. Kept internal
/// because which connection the message rides on is the only real difference between the two.
/// </summary>
internal sealed class PgmqDispatcher(PgmqSinkOptions options)
{
    public async Task SendAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        IntegrationEventMessage message,
        Func<CancellationToken, ValueTask<NpgsqlConnection>> openSeparately,
        CancellationToken cancellationToken)
    {
        var queue = options.QueueName(message);

        if (string.IsNullOrWhiteSpace(queue))
        {
            throw new InvalidOperationException($"The queue name for message {message.MessageId} ('{message.Name}') came back empty. A pgmq queue name cannot be empty.");
        }

        var key = $"{connection.Database}|{queue}";

        if (options.CreateQueueIfMissing && !options.KnownQueues.ContainsKey(key))
        {
            await EnsureQueueAsync(connection, transaction, queue, openSeparately, cancellationToken).ConfigureAwait(false);
            options.KnownQueues[key] = true;
        }

        await PgmqQueue.SendAsync(connection, transaction, queue, message.Payload, Headers(message), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates the queue on a connection of its own, so it survives a rollback of whatever transaction
    /// the message is being enqueued in. Checked for the extension first, so a database without pgmq says
    /// so instead of failing on a create that could never have worked.
    /// </summary>
    private static async Task EnsureQueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string queue,
        Func<CancellationToken, ValueTask<NpgsqlConnection>> openSeparately,
        CancellationToken cancellationToken)
    {
        // The check reads, so it can ride the caller's transaction; the create must not.
        await PgmqQueue.EnsureInstalledAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        await using var own = await openSeparately(cancellationToken).ConfigureAwait(false);
        await PgmqQueue.CreateAsync(own, transaction: null, queue, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The envelope's routing fields as JSON, so a consumer can filter on the name or the version and a
    /// human reading the table can tell what a row is without parsing the body.
    /// </summary>
    private string? Headers(IntegrationEventMessage message)
    {
        if (!options.SendHeaders)
        {
            return null;
        }

        return JsonSerializer.Serialize(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["messageId"] = message.MessageId.ToString(),
            ["name"] = message.Name,
            ["version"] = message.Version.ToString(),
            ["contentType"] = message.ContentType,
            ["occurredAt"] = message.OccurredAt.ToString("O"),
            ["aggregateType"] = message.AggregateType,
            ["aggregateId"] = message.AggregateId,
        });
    }
}
