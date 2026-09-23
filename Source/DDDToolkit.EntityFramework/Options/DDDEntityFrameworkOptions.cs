using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.Interfaces;
using Microsoft.EntityFrameworkCore;

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
/// <para>
/// <b>One object, many contributors.</b> <c>AddDDDToolkitEntityFramework</c> may be called more than
/// once, and every call configures this same object. That is what lets each module of a modular
/// monolith register its own outbox (<see cref="UseOutbox{TContext}"/>) and the contracts it reads
/// (<see cref="MapIntegrationEvents"/>) next to its own <c>DbContext</c>, while the host sets what is
/// genuinely process-wide, such as <see cref="DispatchInProcess"/>.
/// </para>
/// </summary>
public sealed class DDDEntityFrameworkOptions
{
    private readonly Dictionary<Type, OutboxOptions> _contextOutboxes = [];

    /// <summary>The delegate handlers are invoked through, or <see langword="null"/> when not configured.</summary>
    public Func<IServiceProvider, IReadOnlyList<IDomainEvent>, CancellationToken, Task>? Dispatcher { get; private set; }

    /// <summary>
    /// The outbox every context uses unless it has one of its own (<see cref="UseOutbox{TContext}"/>),
    /// or <see langword="null"/> when there is none. Ask <see cref="OutboxFor"/> which one a given context
    /// gets.
    /// </summary>
    public OutboxOptions? Outbox { get; private set; }

    /// <summary>The contexts that have an outbox of their own, and its configuration.</summary>
    public IReadOnlyDictionary<Type, OutboxOptions> ContextOutboxes => _contextOutboxes;

    /// <summary>True when any outbox is configured: the shared one, or one for a single context.</summary>
    public bool OutboxEnabled => Outbox is not null || _contextOutboxes.Count > 0;

    /// <summary>
    /// The outbox <paramref name="contextType"/> writes to: its own when it has one, otherwise the shared
    /// <see cref="Outbox"/>, otherwise <see langword="null"/>, which means its events are dispatched in
    /// process. A context derived from one that has an outbox inherits it.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="contextType"/> is null.</exception>
    public OutboxOptions? OutboxFor(Type contextType)
    {
        ArgumentNullException.ThrowIfNull(contextType);

        for (var type = contextType; type is not null; type = type.BaseType)
        {
            if (_contextOutboxes.TryGetValue(type, out var own))
            {
                return own;
            }
        }

        return Outbox;
    }

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
    /// <para>
    /// There is one delegate per process, so this can be called once. A second call throws rather than
    /// replace the first, because with registration spread over several modules a silent replacement
    /// would hand one module's events to another module's publisher without anybody noticing.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="dispatcher"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A dispatch delegate is already configured.</exception>
    public DDDEntityFrameworkOptions DispatchInProcess(Func<IServiceProvider, IReadOnlyList<IDomainEvent>, CancellationToken, Task> dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        if (Dispatcher is not null)
        {
            throw new InvalidOperationException(
                "An in-process dispatch delegate is already configured, and there is one per process. " +
                "Call DispatchInProcess (or DispatchWithMediator) once, in the host, and let the modules configure their outboxes and contracts.");
        }

        Dispatcher = dispatcher;
        return this;
    }

    /// <summary>
    /// Stores events in the outbox table of the saving <c>DbContext</c> instead of dispatching them at
    /// save time. The context's model must include the table: call
    /// <c>modelBuilder.AddDomainEventOutbox()</c> in <c>OnModelCreating</c>. Register the event types the
    /// processor may encounter with <see cref="OutboxOptions.RegisterEventsFromAssembly"/>, and say
    /// where the messages go with <see cref="OutboxOptions.SendTo{TSink}()"/>.
    /// <para>
    /// This outbox is shared by every context that has none of its own. In an application with one
    /// context that is all you need; with a context per module, give each producing module its own with
    /// <see cref="UseOutbox{TContext}"/>. Calling this again configures the same shared outbox further.
    /// </para>
    /// </summary>
    public DDDEntityFrameworkOptions UseOutbox(Action<OutboxOptions>? configure = null)
    {
        Outbox ??= new OutboxOptions();
        configure?.Invoke(Outbox);
        return this;
    }

    /// <summary>
    /// Gives <typeparamref name="TContext"/> an outbox of its own: its event types, its published
    /// contracts, its sinks. Its events are written to its own outbox table and delivered by
    /// <c>OutboxProcessor&lt;TContext&gt;</c> with this configuration, whatever the shared
    /// <see cref="UseOutbox(Action{OutboxOptions}?)"/> says.
    /// <para>
    /// This is the registration for a module that publishes. The module states what it publishes and
    /// where to, next to its own context, and neither the host nor the consuming modules need to know.
    /// Calling it again for the same context configures the same outbox further.
    /// </para>
    /// <code>
    /// services.AddDDDToolkitEntityFramework(options => options.UseOutbox&lt;OrderingContext&gt;(outbox =>
    /// {
    ///     outbox.RegisterEventsFromAssemblyContaining&lt;Order&gt;();
    ///     outbox.PublishAs&lt;OrderPlaced, OrderPlacedV1&gt;(e =&gt; new OrderPlacedV1(...));
    ///     outbox.SendToModules();
    /// }));
    /// </code>
    /// </summary>
    public DDDEntityFrameworkOptions UseOutbox<TContext>(Action<OutboxOptions>? configure = null) where TContext : DbContext
    {
        if (!_contextOutboxes.TryGetValue(typeof(TContext), out var outbox))
        {
            _contextOutboxes[typeof(TContext)] = outbox = new OutboxOptions();
        }

        configure?.Invoke(outbox);
        return this;
    }
}
