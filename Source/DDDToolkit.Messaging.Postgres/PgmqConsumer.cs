using System.Text.Json;
using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Integration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace DDDToolkit.Messaging.Postgres;

/// <summary>How a <see cref="PgmqConsumer"/> reads its queue.</summary>
public sealed class PgmqConsumerOptions
{
    /// <summary>
    /// How long a message stays hidden from other readers once read. A message not archived by then is
    /// visible again, which is how a failed delivery is retried. Longer than the slowest handler.
    /// </summary>
    public TimeSpan VisibilityTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How many messages one read takes.</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>How long to wait before reading again when the queue was empty.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How often a message may be read and fail before it is archived as poison instead of retried. It
    /// stays in the archive table, where it can be read and replayed by hand.
    /// </summary>
    public int MaxDeliveries { get; set; } = 10;
}

/// <summary>
/// Reads a pgmq queue and hands each message to the modules in this process, through
/// <see cref="IntegrationEventReceiver"/>. The receiving end of <see cref="PgmqSink{TContext}"/>.
/// </summary>
/// <remarks>
/// Delivery is at least once, in the usual way for a queue: a message is archived only after every
/// consuming module applied it, and each module's inbox makes a second delivery a no-op. A message whose
/// handlers throw is left where it is and becomes visible again after
/// <see cref="PgmqConsumerOptions.VisibilityTimeout"/>, so a failure retries by waiting and nothing more.
/// After <see cref="PgmqConsumerOptions.MaxDeliveries"/> reads it is archived as poison, logged as an
/// error, so one broken message cannot block the queue for ever.
/// <para>
/// A message without the toolkit's headers cannot be given an identity to deduplicate on, so it is
/// archived at once and logged: it did not come from a toolkit outbox.
/// </para>
/// </remarks>
public sealed class PgmqConsumer : BackgroundService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _queue;
    private readonly IntegrationEventReceiver _receiver;
    private readonly PgmqConsumerOptions _options;
    private readonly ILogger _logger;

    /// <summary>Creates a consumer of <paramref name="queue"/>.</summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="queue"/> is empty or white space.</exception>
    public PgmqConsumer(
        NpgsqlDataSource dataSource,
        string queue,
        IntegrationEventReceiver receiver,
        PgmqConsumerOptions options,
        ILogger<PgmqConsumer>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);

        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _queue = queue;
        _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<PgmqConsumer>.Instance;
    }

    /// <summary>
    /// Reads one batch and delivers it. Returns how many messages were read, so a caller polling by hand,
    /// a test for instance, knows when the queue is empty.
    /// </summary>
    public async Task<int> ConsumeOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var messages = await PgmqQueue.ReadAsync(
            connection,
            transaction: null,
            _queue,
            visibilityTimeout: (int)Math.Ceiling(_options.VisibilityTimeout.TotalSeconds),
            count: _options.BatchSize,
            cancellationToken).ConfigureAwait(false);

        foreach (var message in messages)
        {
            await DeliverAsync(connection, message, cancellationToken).ConfigureAwait(false);
        }

        return messages.Count;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using (var connection = await _dataSource.OpenConnectionAsync(stoppingToken).ConfigureAwait(false))
        {
            // The queue may be read before anybody sent to it: a consumer that starts first is normal.
            await PgmqQueue.CreateAsync(connection, transaction: null, _queue, stoppingToken).ConfigureAwait(false);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await ConsumeOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Reading pgmq queue '{Queue}' failed; trying again.", _queue);
                read = 0;
            }

            if (read == 0)
            {
                await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task DeliverAsync(NpgsqlConnection connection, PgmqMessage row, CancellationToken cancellationToken)
    {
        IntegrationEventMessage message;
        try
        {
            var headers = row.Headers is null
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, string?>>(row.Headers);

            message = IntegrationEventHeaders.ToMessage(headers ?? [], row.Body);
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            _logger.LogError(exception, "pgmq message {Row} on '{Queue}' has no usable envelope headers and is archived unread.", row.MessageId, _queue);
            await PgmqQueue.ArchiveAsync(connection, transaction: null, _queue, row.MessageId, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await _receiver.ReceiveAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (row.ReadCount >= _options.MaxDeliveries)
            {
                _logger.LogError(exception, "Message {MessageId} ('{Name}') failed {Reads} times and is archived as poison on '{Queue}'.", message.MessageId, message.Name, row.ReadCount, _queue);
                await PgmqQueue.ArchiveAsync(connection, transaction: null, _queue, row.MessageId, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning(exception, "Message {MessageId} ('{Name}') failed on delivery {Reads}; it is retried after the visibility timeout.", message.MessageId, message.Name, row.ReadCount);
            }

            return;
        }

        await PgmqQueue.ArchiveAsync(connection, transaction: null, _queue, row.MessageId, cancellationToken).ConfigureAwait(false);
    }
}
