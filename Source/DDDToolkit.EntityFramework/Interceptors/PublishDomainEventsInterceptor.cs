using System.Text.Json;
using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DDDToolkit.EntityFramework.Interceptors;

/// <summary>
/// Delivers the domain events of every tracked aggregate when <c>SaveChanges</c> is called. Both the
/// sync and the async overload behave the same: the events are handled <em>before</em> the database
/// is written, so whatever a handler changes on the same <c>DbContext</c> rides the same save and a
/// throwing handler aborts it.
/// <para>
/// <b>In-process mode</b> (<see cref="DDDEntityFrameworkOptions.DispatchInProcess"/>): events are dequeued
/// from all tracked <see cref="IHasDomainEvents"/> entries and passed to the dispatch delegate; if the
/// handlers raised new events on tracked aggregates the loop repeats, up to
/// <see cref="DDDEntityFrameworkOptions.MaxDispatchRounds"/> rounds. Delivery is best-effort: once
/// dequeued, an event only exists in the handlers' memory. A handler that throws aborts the save and
/// the events are gone; nothing outside the database is rolled back.
/// </para>
/// <para>
/// <b>Outbox mode</b> (<see cref="DDDEntityFrameworkOptions.UseOutbox"/>): instead of dispatching, one
/// <see cref="OutboxMessage"/> per event is added to the saving context, so it commits atomically with
/// the aggregate. <see cref="OutboxProcessor{TContext}"/> delivers it later, at least once.
/// </para>
/// <para>
/// The synchronous <c>SaveChanges</c> blocks on the asynchronous dispatch delegate. That is safe in
/// console and ASP.NET Core applications (no synchronization context) but can deadlock under a UI or
/// legacy ASP.NET synchronization context; prefer <c>SaveChangesAsync</c>.
/// </para>
/// </summary>
public sealed class PublishDomainEventsInterceptor : SaveChangesInterceptor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DDDEntityFrameworkOptions _options;

    /// <param name="serviceProvider">
    /// The (preferably scoped) application service provider handed to the dispatch delegate. When the
    /// interceptor is resolved from the scope that also resolves the <c>DbContext</c>, handlers that
    /// inject the context get the very instance being saved.
    /// </param>
    /// <param name="options">The delivery configuration.</param>
    public PublishDomainEventsInterceptor(IServiceProvider serviceProvider, DDDEntityFrameworkOptions options)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            // Documented: the sync path blocks on the async delegate.
            DeliverAsync(context, CancellationToken.None).GetAwaiter().GetResult();
        }

        return result;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            await DeliverAsync(context, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private async Task DeliverAsync(DbContext context, CancellationToken cancellationToken)
    {
        for (var round = 1; ; round++)
        {
            var batch = Dequeue(context);
            if (batch.Count == 0)
            {
                return;
            }

            if (round > _options.MaxDispatchRounds)
            {
                var pending = string.Join(", ", batch.SelectMany(item => item.Events).Select(DomainEventName.Of).Distinct(StringComparer.Ordinal));
                throw new InvalidOperationException(
                    $"Domain events were still being raised after {_options.MaxDispatchRounds} dispatch rounds; a handler probably raises an event that triggers itself. " +
                    $"Events still pending: {pending}. Raise {nameof(DDDEntityFrameworkOptions)}.{nameof(DDDEntityFrameworkOptions.MaxDispatchRounds)} if the chain is intentional.");
            }

            if (_options.Outbox is { } outbox)
            {
                WriteToOutbox(context, batch, outbox);
                continue;
            }

            var dispatcher = _options.Dispatcher
                ?? throw new InvalidOperationException(
                    $"{batch.Sum(item => item.Events.Count)} domain event(s) are pending on {string.Join(", ", batch.Select(item => item.Entry.Metadata.ClrType.Name).Distinct())} but no delivery mode is configured. " +
                    $"Call {nameof(DDDEntityFrameworkOptions.DispatchInProcess)}(...) or {nameof(DDDEntityFrameworkOptions.UseOutbox)}(...) in AddDDDToolkitEntityFramework.");

            var events = batch.SelectMany(item => item.Events).ToList();
            await dispatcher(_serviceProvider, events, cancellationToken).ConfigureAwait(false);

            // Handlers may have modified tracked entities without EF noticing yet; detect so the
            // save (and the concurrency interceptor) sees those changes and the next round sees new events.
            if (context.ChangeTracker.AutoDetectChangesEnabled)
            {
                context.ChangeTracker.DetectChanges();
            }
        }
    }

    private static List<(EntityEntry Entry, IReadOnlyList<IDomainEvent> Events)> Dequeue(DbContext context)
    {
        var batch = new List<(EntityEntry, IReadOnlyList<IDomainEvent>)>();

        foreach (var entry in context.ChangeTracker.Entries<IHasDomainEvents>().ToList())
        {
            var events = entry.Entity.DequeueDomainEvents();
            if (events.Count > 0)
            {
                batch.Add((entry, events));
            }
        }

        return batch;
    }

    private void WriteToOutbox(DbContext context, List<(EntityEntry Entry, IReadOnlyList<IDomainEvent> Events)> batch, OutboxOptions outbox)
    {
        if (context.Model.FindEntityType(typeof(OutboxMessage)) is null)
        {
            throw new InvalidOperationException(
                $"UseOutbox is configured but the model of '{context.GetType().Name}' does not contain the outbox table. " +
                $"Call modelBuilder.{nameof(OutboxModelBuilderExtensions.AddDomainEventOutbox)}() in OnModelCreating.");
        }

        var now = _options.TimeProvider.GetUtcNow();

        foreach (var (entry, events) in batch)
        {
            var aggregateType = entry.Metadata.ClrType.Name;
            var aggregateId = DescribeKey(entry);

            foreach (var domainEvent in events)
            {
                context.Add(new OutboxMessage
                {
                    Id = domainEvent.EventId,
                    EventName = DomainEventName.Of(domainEvent),
                    // The shape, not just the name: a row read after a deployment has to say which
                    // version of the event it was written as. Defaults to 1 for an event that never
                    // carried [IntegrationEvent].
                    Version = IntegrationEventContract.VersionOf(domainEvent.GetType()),
                    Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), outbox.JsonOptions),
                    OccurredAt = domainEvent.OccurredAt,
                    AggregateType = aggregateType,
                    AggregateId = aggregateId,
                    CreatedAt = now,
                });
            }
        }
    }

    private static string? DescribeKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
        {
            return null;
        }

        var values = key.Properties.Select(property => entry.Property(property.Name).CurrentValue?.ToString());
        return string.Join("|", values);
    }
}
