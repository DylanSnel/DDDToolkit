using System.Text.Json;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DDDToolkit.EntityFramework.Outbox;

/// <summary>
/// Delivers pending <see cref="OutboxMessage"/> rows of <typeparamref name="TContext"/>: oldest first,
/// each deserialized through the <see cref="DomainEventTypeRegistry"/> and handed to the dispatch
/// delegate configured with <see cref="DDDEntityFrameworkOptions.DispatchInProcess"/>.
/// <para>
/// Guarantees: at-least-once. A message is marked processed (and saved) only after its handlers
/// returned; a crash in between redelivers it, and two processors racing on the same table may both
/// deliver a row. Handlers must therefore be idempotent, keyed by <see cref="IDomainEvent.EventId"/>
/// (= <see cref="OutboxMessage.Id"/>). A handler that throws, or an event name nobody registered, is
/// recorded on the row (<see cref="OutboxMessage.LastError"/>, <see cref="OutboxMessage.Attempts"/>)
/// and does not stop the rest of the batch; rows that reached <see cref="OutboxOptions.MaxAttempts"/>
/// are skipped until <c>Attempts</c> is reset.
/// </para>
/// <para>
/// Each message is saved individually through the same context, so when a handler resolves that
/// scoped context and changes an aggregate, its change commits together with the processed mark.
/// </para>
/// Register with <c>services.AddOutboxProcessor&lt;TContext&gt;()</c>, or let
/// <see cref="OutboxBackgroundService{TContext}"/> poll it.
/// </summary>
public sealed class OutboxProcessor<TContext> where TContext : DbContext
{
    private readonly TContext _context;
    private readonly IServiceProvider _serviceProvider;
    private readonly DDDEntityFrameworkOptions _options;
    private readonly ILogger _logger;

    /// <summary>Creates a processor for the (scoped) <paramref name="context"/>.</summary>
    public OutboxProcessor(TContext context, IServiceProvider serviceProvider, DDDEntityFrameworkOptions options, ILogger<OutboxProcessor<TContext>>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<OutboxProcessor<TContext>>.Instance;

        if (_options.Outbox is null)
        {
            throw new InvalidOperationException($"The outbox is not enabled. Call {nameof(DDDEntityFrameworkOptions.UseOutbox)}(...) in AddDDDToolkitEntityFramework.");
        }

        if (_options.Dispatcher is null)
        {
            throw new InvalidOperationException($"The outbox processor delivers through the dispatch delegate; call {nameof(DDDEntityFrameworkOptions.DispatchInProcess)}(...) in AddDDDToolkitEntityFramework.");
        }
    }

    /// <summary>
    /// Loads up to <paramref name="batchSize"/> pending messages, oldest first, and dispatches them.
    /// Returns the number of messages delivered successfully.
    /// </summary>
    public async Task<int> ProcessPendingAsync(int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        var outbox = _options.Outbox!;
        var dispatcher = _options.Dispatcher!;
        var maxAttempts = outbox.MaxAttempts;

        var messages = await _context.Set<OutboxMessage>()
            .Where(message => message.ProcessedAt == null && message.Attempts < maxAttempts)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var processed = 0;

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            message.Attempts++;

            try
            {
                var domainEvent = Deserialize(message, outbox);
                await dispatcher(_serviceProvider, [domainEvent], cancellationToken).ConfigureAwait(false);

                message.ProcessedAt = _options.TimeProvider.GetUtcNow();
                message.LastError = null;
                processed++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                message.LastError = Describe(exception);
                _logger.LogError(exception, "Delivery of outbox message {MessageId} ({EventName}) failed on attempt {Attempt}.", message.Id, message.EventName, message.Attempts);
            }

            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return processed;
    }

    private static IDomainEvent Deserialize(OutboxMessage message, OutboxOptions outbox)
    {
        if (!outbox.EventTypes.TryResolve(message.EventName, out var eventType))
        {
            throw new InvalidOperationException(
                $"No domain event type is registered under the name '{message.EventName}'. Register it with outbox.RegisterEventsFromAssembly(...) or outbox.RegisterEvent<T>().");
        }

        return JsonSerializer.Deserialize(message.Payload, eventType, outbox.JsonOptions) as IDomainEvent
            ?? throw new JsonException($"The payload of outbox message {message.Id} deserialized to null.");
    }

    private static string Describe(Exception exception)
    {
        var text = $"{exception.GetType().FullName}: {exception.Message}";
        return text.Length <= OutboxProcessorDefaults.MaxErrorLength ? text : text[..OutboxProcessorDefaults.MaxErrorLength];
    }
}
