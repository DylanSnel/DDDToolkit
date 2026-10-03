using System.Text.Json;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.EventLog;
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
/// the aggregate. <see cref="OutboxProcessor{TContext}"/> delivers it later, at least once. Where the
/// outbox keeps an event log (<see cref="OutboxOptions.KeepEventLog"/>), one <see cref="EventLogEntry"/> per
/// kept event is added in the same way, with who acted on it, so it commits or rolls back with both.
/// </para>
/// <para>
/// The synchronous <c>SaveChanges</c> blocks on the asynchronous dispatch delegate. That is safe in
/// console and ASP.NET Core applications (no synchronization context) but can deadlock under a UI or
/// legacy ASP.NET synchronization context; prefer <c>SaveChangesAsync</c>.
/// </para>
/// <para>
/// <b>Whose services the in-process handlers get</b> is decided per save, for the context that is saving.
/// First the scope the context was bound to, with <see cref="PooledContexts.BindToScope{TContext}"/>, which
/// <see cref="PooledContexts.AddScopedFromPool{TContext}"/> calls for the context a scope takes from a pool.
/// Without one, a context that is not pooled gets the provider this interceptor was built with, which with
/// <c>AddDbContext</c> is the scope that owns the context. A pooled context that was given no scope is
/// refused, before any event leaves its aggregate: a pool's options are built once, with the application's
/// root services, and a handler that asked those for the context would be given another one than the one
/// being saved. A provider that turns out to be the root is refused the same way, where the container
/// validates scopes. The outbox asks for no scoped service, so it works from every context, bound or not.
/// The event log is written on that same path: who acted (<see cref="IActedByAccessor"/>) and the columns a
/// module added (<see cref="IEventLogFields"/>) come from singletons that read what they answer each time they
/// are asked, taken once per save from the provider this interceptor was built with, which is the root one
/// under a pool. So a kept event is written from a pooled context without a scope as well.
/// </para>
/// </summary>
public sealed class PublishDomainEventsInterceptor : SaveChangesInterceptor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DDDEntityFrameworkOptions _options;

    /// <summary>
    /// Whether this interceptor's own provider was asked for a scoped service and gave no refusal, so it may be
    /// handed to handlers. Asked until it answers so, on a dispatch that needs it, and not again after.
    /// </summary>
    private volatile bool _providerIsScoped;

    /// <param name="serviceProvider">
    /// The provider handed to the options callback, which <c>UseDDDToolkit</c> passes on. With
    /// <c>AddDbContext</c> it is the scope that owns the context, so handlers that inject the context get the
    /// very instance being saved. With a context pool it is the application's root provider, and the handlers
    /// get the scope each rental was bound to instead.
    /// </param>
    /// <param name="options">The delivery configuration.</param>
    public PublishDomainEventsInterceptor(IServiceProvider serviceProvider, DDDEntityFrameworkOptions options)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>The delivery configuration this interceptor saves with, which a wiring check reads.</summary>
    internal DDDEntityFrameworkOptions Options => _options;

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
        var outbox = _options.OutboxFor(context.GetType());
        IServiceProvider? services = null;
        EventLogWriting? logging = null;

        for (var round = 1; ; round++)
        {
            var tracked = Tracked(context);

            // Picked on the first round that has something to dispatch, and before it is dequeued: a context
            // that may not dispatch keeps its events, so a refusal loses none.
            if (outbox is null && services is null && _options.Dispatcher is not null && HasPending(tracked))
            {
                services = ServicesFor(context);
            }

            // Asked before anything is dequeued as well, and once per save: a save that cannot say who acted
            // keeps its events.
            if (outbox?.EventLog is { } log && logging is null && KeepsPending(tracked, log))
            {
                logging = EventLogWritingFor(context);
            }

            var batch = Dequeue(tracked);
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

            if (outbox is not null)
            {
                // Rows on the saving context, and no scoped service: any context can write them.
                WriteToOutbox(context, batch, outbox, logging);
                continue;
            }

            var dispatcher = _options.Dispatcher
                ?? throw new InvalidOperationException(
                    $"{batch.Sum(item => item.Events.Count)} domain event(s) are pending on {string.Join(", ", batch.Select(item => item.Entry.Metadata.ClrType.Name).Distinct())} but no delivery mode is configured. " +
                    $"Call {nameof(DDDEntityFrameworkOptions.DispatchInProcess)}(...) or {nameof(DDDEntityFrameworkOptions.UseOutbox)}(...) in AddDDDToolkitEntityFramework.");

            // A batch has events only where something was pending above, so the services were picked.
            var events = batch.SelectMany(item => item.Events).ToList();
            await dispatcher(services!, events, cancellationToken).ConfigureAwait(false);

            // Handlers may have modified tracked entities without EF noticing yet; detect so the
            // save (and the concurrency interceptor) sees those changes and the next round sees new events.
            if (context.ChangeTracker.AutoDetectChangesEnabled)
            {
                context.ChangeTracker.DetectChanges();
            }
        }
    }

    /// <summary>
    /// The services the in-process handlers of <paramref name="context"/>'s events run with: the scope this
    /// rental was bound to, or, for a context that is not pooled, this interceptor's own provider once it was
    /// seen to be a scope's.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The context is pooled and this rental was given no scope, or this interceptor's provider is the
    /// application's root one.
    /// </exception>
    private IServiceProvider ServicesFor(DbContext context)
    {
        if (ScopeBinding.Find(context) is { } scope)
        {
            return scope;
        }

        var name = context.GetType().Name;

        if (context.IsPooled())
        {
            // Not a scope made here instead: a handler that asked it for the context would be given another
            // instance than the one being saved, and what it changed would go nowhere, without a word.
            throw new InvalidOperationException(
                $"'{name}' was taken from a context pool and this rental was given no scope, so the handlers of its pending domain events have no scoped services to run with. " +
                $"Take the context from a scope, which services.{nameof(PooledContexts.AddScopedFromPool)}<{name}>() registers, " +
                $"or call context.{nameof(PooledContexts.BindToScope)}(scope.ServiceProvider) after CreateDbContext(); " +
                $"or store the events instead, with {nameof(DDDEntityFrameworkOptions.UseOutbox)}<{name}>(...). " +
                "Nothing was dispatched and nothing was saved.");
        }

        EnsureScoped(name);
        return _serviceProvider;
    }

    /// <summary>
    /// Refuses a provider that is the application's root one, where the container can tell. With singleton
    /// options, <c>AddDbContext(..., optionsLifetime: ServiceLifetime.Singleton)</c> or
    /// <c>AddDbContextFactory</c>, the options callback is handed the root, and a handler given the root
    /// would resolve scoped services that live as long as the application. A container that validates scopes
    /// refuses to resolve a scoped service from its root, which is what is asked here; one that does not
    /// validate, and a provider the marker was never registered with, answer as they always did.
    /// </summary>
    private void EnsureScoped(string context)
    {
        if (_providerIsScoped)
        {
            return;
        }

        try
        {
            _ = _serviceProvider.GetService(typeof(ScopeMarker));
        }
        catch (InvalidOperationException exception) when (exception is not ObjectDisposedException)
        {
            throw new InvalidOperationException(
                $"The options of '{context}' were built with the application's root services, so the handlers of its domain events would be given services that outlive every scope. " +
                $"Leave the options of AddDbContext scoped, or take the context from a pool and a scope (AddPooledDbContextFactory with {nameof(PooledContexts.AddScopedFromPool)}), " +
                $"or call context.{nameof(PooledContexts.BindToScope)}(scope.ServiceProvider). Nothing was dispatched and nothing was saved.",
                exception);
        }

        _providerIsScoped = true;
    }

    /// <summary>The tracked aggregates, listed once per round, because listing them makes Entity Framework detect changes.</summary>
    private static List<EntityEntry<IHasDomainEvents>> Tracked(DbContext context)
        => context.ChangeTracker.Entries<IHasDomainEvents>().ToList();

    private static bool HasPending(List<EntityEntry<IHasDomainEvents>> tracked)
    {
        foreach (var entry in tracked)
        {
            if (entry.Entity.DomainEvents.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether an event still on its aggregate is one <paramref name="log"/> keeps.</summary>
    private static bool KeepsPending(List<EntityEntry<IHasDomainEvents>> tracked, EventLogOptions log)
    {
        foreach (var entry in tracked)
        {
            foreach (var domainEvent in entry.Entity.DomainEvents)
            {
                if (log.Keeps(domainEvent))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static List<(EntityEntry Entry, IReadOnlyList<IDomainEvent> Events)> Dequeue(List<EntityEntry<IHasDomainEvents>> tracked)
    {
        var batch = new List<(EntityEntry, IReadOnlyList<IDomainEvent>)>();

        foreach (var entry in tracked)
        {
            var events = entry.Entity.DequeueDomainEvents();
            if (events.Count > 0)
            {
                batch.Add((entry, events));
            }
        }

        return batch;
    }

    private void WriteToOutbox(DbContext context, List<(EntityEntry Entry, IReadOnlyList<IDomainEvent> Events)> batch, OutboxOptions outbox, EventLogWriting? logging)
    {
        if (context.Model.FindEntityType(typeof(OutboxMessage)) is null)
        {
            throw new InvalidOperationException(
                $"An outbox is configured for '{context.GetType().Name}' but its model does not contain the outbox table. " +
                $"Call modelBuilder.{nameof(OutboxModelBuilderExtensions.AddDomainEventOutbox)}() in OnModelCreating.");
        }

        var log = outbox.EventLog;
        var now = _options.TimeProvider.GetUtcNow();

        foreach (var (entry, events) in batch)
        {
            var aggregateType = entry.Metadata.ClrType.Name;
            var aggregateId = DescribeKey(entry);

            foreach (var domainEvent in events)
            {
                // The name and version the event was registered under, which the generated registration
                // wrote down at compile time. Only an event nobody registered is asked for its attributes.
                if (!outbox.EventTypes.TryDescribe(domainEvent.GetType(), out var eventName, out var version))
                {
                    eventName = DomainEventName.Of(domainEvent);
                    version = IntegrationEventContract.VersionOf(domainEvent.GetType());
                }

                var payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), outbox.JsonOptions);

                context.Add(new OutboxMessage
                {
                    Id = domainEvent.EventId,
                    EventName = eventName,
                    // The shape, not just the name: a row read after a deployment has to say which
                    // version of the event it was written as. Defaults to 1 for an event that never
                    // carried [IntegrationEvent].
                    Version = version,
                    Payload = payload,
                    OccurredAt = domainEvent.OccurredAt,
                    AggregateType = aggregateType,
                    AggregateId = aggregateId,
                    CreatedAt = now,
                });

                if (log is null || !log.Keeps(domainEvent))
                {
                    continue;
                }

                // Asked before the events left their aggregates, wherever one of them is kept; here for an event
                // that only showed itself afterwards.
                logging ??= EventLogWritingFor(context);

                // The same facts as the outbox row, and none of its delivery state: this row is added here and
                // never touched again.
                var row = context.Add(new EventLogEntry
                {
                    Id = domainEvent.EventId,
                    EventName = eventName,
                    Version = version,
                    Payload = payload,
                    OccurredAt = domainEvent.OccurredAt,
                    RecordedAt = now,
                    AggregateType = aggregateType,
                    AggregateId = aggregateId,
                    ActedByKind = logging.ActedBy.Kind,
                    ActedById = logging.ActedBy.Id,
                });

                foreach (var fields in logging.Fields)
                {
                    fields.Fill(domainEvent, row);
                }
            }
        }
    }

    /// <summary>What every kept event of one save is written with: who is acting, and what fills the columns a module added to its log.</summary>
    private sealed record EventLogWriting(ActedBy ActedBy, IReadOnlyList<IEventLogFields> Fields);

    /// <summary>
    /// Who is acting in this save, and what fills the columns a module added to its log: asked of this
    /// interceptor's own provider, not of a scope bound to the context, so the log is written from a pooled
    /// context that was given no scope as well. Both are singletons that read what they answer when asked.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The context's model has no event log, or one of them is registered as a scoped service and the provider is
    /// the application's root one.
    /// </exception>
    private EventLogWriting EventLogWritingFor(DbContext context)
    {
        if (context.Model.FindEntityType(typeof(EventLogEntry)) is null)
        {
            throw new InvalidOperationException(
                $"The outbox of '{context.GetType().Name}' keeps an event log but the context's model does not contain the event log table. " +
                $"Call modelBuilder.{nameof(EventLogModelBuilderExtensions.AddEventLog)}(Database) in OnModelCreating, or take {nameof(OutboxOptions.KeepEventLog)}() out. " +
                "Nothing was saved.");
        }

        IActedByAccessor? accessor;
        IReadOnlyList<IEventLogFields> fields;
        try
        {
            accessor = (IActedByAccessor?)_serviceProvider.GetService(typeof(IActedByAccessor));
            fields = [.. (IEnumerable<IEventLogFields>?)_serviceProvider.GetService(typeof(IEnumerable<IEventLogFields>)) ?? []];
        }
        catch (InvalidOperationException exception) when (exception is not ObjectDisposedException)
        {
            throw new InvalidOperationException(
                $"The event log of '{context.GetType().Name}' could not be written: {nameof(IActedByAccessor)} or an {nameof(IEventLogFields)} is registered as a scoped service, " +
                "and the options of the context were built with the application's root services, as those of a context pool are. " +
                "Register them as singletons that read what they answer each time they are asked. Nothing was saved.",
                exception);
        }

        // A host that built this interceptor by hand, without the toolkit's registration, still gets the toolkit's own answer.
        accessor ??= new CallerActedByAccessor((ICallerAccessor?)_serviceProvider.GetService(typeof(ICallerAccessor)));
        return new EventLogWriting(accessor.Current, fields);
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
