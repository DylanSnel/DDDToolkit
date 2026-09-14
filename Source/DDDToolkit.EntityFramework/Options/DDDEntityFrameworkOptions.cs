using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.Interfaces;

namespace DDDToolkit.EntityFramework.Options;

/// <summary>
/// Configures how DDDToolkit.EntityFramework delivers domain events. Configure it through
/// <c>services.AddDDDToolkitEntityFramework(options =&gt; ...)</c>.
/// <para>
/// There are two delivery modes; pick exactly one for production:
/// </para>
/// <list type="bullet">
///   <item>
///     <term><see cref="DispatchInProcess"/></term>
///     <description>
///     Handlers run inside <c>SaveChanges</c>, before the database is written. Anything a handler
///     changes on the same <c>DbContext</c> rides the same save (and therefore the same transaction),
///     and a throwing handler aborts the save. Delivery is best-effort: the events are dequeued from
///     the aggregate before dispatch, so when a handler throws, the save is rolled back and the events
///     are gone. A crash after the handlers ran but before the commit loses nothing in the database but
///     side effects the handlers had outside it (mails, HTTP calls) have already happened.
///     </description>
///   </item>
///   <item>
///     <term><see cref="UseOutbox"/></term>
///     <description>
///     Events are serialized into an outbox table by the same <c>SaveChanges</c> that writes the
///     aggregate, so they commit atomically with it. A separate <c>OutboxProcessor&lt;TContext&gt;</c>
///     (or <c>OutboxBackgroundService&lt;TContext&gt;</c>) delivers them later, either through the
///     <see cref="DispatchInProcess"/> delegate or, when <c>outbox.SendTo&lt;TSink&gt;()</c> names one
///     or more sinks, out of the process as an <c>IntegrationEventMessage</c>. This is at-least-once
///     delivery: a message is marked processed only after everything it was handed to succeeded, so a
///     crash in between redelivers it. Consumers must be idempotent, keyed by
///     <see cref="IDomainEvent.EventId"/>.
///     </description>
///   </item>
/// </list>
/// When the outbox is enabled nothing is dispatched at save time; the dispatch delegate is used by
/// the processor only, and only while no sink is configured or
/// <see cref="OutboxOptions.AlsoDispatchInProcess"/> asks for both. When neither mode is configured
/// and an aggregate has pending events, <c>SaveChanges</c> throws instead of silently dropping them.
/// </summary>
public sealed class DDDEntityFrameworkOptions
{
    /// <summary>The delegate handlers are invoked through, or <see langword="null"/> when not configured.</summary>
    public Func<IServiceProvider, IReadOnlyList<IDomainEvent>, CancellationToken, Task>? Dispatcher { get; private set; }

    /// <summary>The outbox configuration, or <see langword="null"/> when events are dispatched in process.</summary>
    public OutboxOptions? Outbox { get; private set; }

    /// <summary>True when <see cref="UseOutbox"/> was called.</summary>
    public bool OutboxEnabled => Outbox is not null;

    /// <summary>
    /// In-process dispatch loops while handlers raise new events on tracked aggregates. This caps the
    /// number of rounds; when it is exceeded <c>SaveChanges</c> throws naming the events still pending.
    /// Defaults to 10.
    /// </summary>
    public int MaxDispatchRounds { get; set; } = 10;

    /// <summary>Clock used for outbox timestamps. Replace it in tests.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// Every payload shape this process can read back, keyed by published name and version, plus the
    /// upcasters between them. Both halves use it: the outbox processor when it reads a stored row, and
    /// the inbox when a consumer reads a delivered message.
    /// <para>
    /// It sits here rather than on <see cref="OutboxOptions"/> because a module that only consumes never
    /// calls <see cref="UseOutbox"/> and still has to read other people's payloads.
    /// </para>
    /// </summary>
    public IntegrationEventContractRegistry Contracts { get; } = new();

    /// <summary>
    /// Configures <see cref="Contracts"/>: which payload shapes this process can read, and how an old
    /// one becomes the current one.
    /// <code>
    /// options.MapIntegrationEvents(contracts => contracts
    ///     .RegisterFromAssemblyContaining&lt;OrderPlacedV2&gt;()
    ///     .UpcastFrom&lt;OrderPlacedV1, OrderPlacedV2&gt;(v1 =&gt; new OrderPlacedV2(v1.OrderId, v1.Total, "EUR")));
    /// </code>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
    public DDDEntityFrameworkOptions MapIntegrationEvents(Action<IntegrationEventContractRegistry> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Contracts);
        return this;
    }

    /// <summary>
    /// Delivers events by invoking <paramref name="dispatcher"/> with the scoped application service
    /// provider and the events dequeued from the aggregates, in the order they were raised.
    /// <para>
    /// Without <see cref="UseOutbox"/> this runs inside <c>SaveChanges</c>, before the database write.
    /// With the outbox it is the delegate the processor delivers through. A typical implementation
    /// resolves the publisher of a mediator library and publishes each event; the DDDToolkit.Mediator
    /// package ships that delegate ready made as <c>options.DispatchWithMediator()</c>.
    /// </para>
    /// </summary>
    public DDDEntityFrameworkOptions DispatchInProcess(Func<IServiceProvider, IReadOnlyList<IDomainEvent>, CancellationToken, Task> dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        Dispatcher = dispatcher;
        return this;
    }

    /// <summary>
    /// Stores events in the outbox table of the saving <c>DbContext</c> instead of dispatching them at
    /// save time. The context's model must include the table: call
    /// <c>modelBuilder.AddDomainEventOutbox()</c> in <c>OnModelCreating</c>. Register the event types the
    /// processor may encounter with <see cref="OutboxOptions.RegisterEventsFromAssembly"/>, and say
    /// where the messages go with <see cref="OutboxOptions.SendTo{TSink}()"/>.
    /// </summary>
    public DDDEntityFrameworkOptions UseOutbox(Action<OutboxOptions>? configure = null)
    {
        Outbox ??= new OutboxOptions();
        configure?.Invoke(Outbox);
        return this;
    }
}
