using System.Collections.Concurrent;
using System.Reflection;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Interfaces;

namespace DDDToolkit.BaseTypes;

/// <summary>
/// Convenient base for domain events: assigns a time-ordered <see cref="EventId"/> and stamps
/// <see cref="OccurredAt"/> with the current UTC time.
/// <para>
/// Both are <c>init</c>, so code that constructs the event can supply its own values. Code that
/// constructs the event is usually the aggregate, not your test, which is why
/// <see cref="DomainEventClock"/> exists: it replaces the clock both initialisers read, for the
/// current asynchronous flow only.
/// </para>
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; init; } = DomainEventClock.NewEventId();

    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; init; } = DomainEventClock.UtcNow;
}

/// <summary>
/// The clock <see cref="DomainEvent"/> stamps itself from. It is <see cref="TimeProvider.System"/>
/// until <see cref="Use(TimeProvider, Func{DateTimeOffset, Guid})"/> installs something else.
/// <para>
/// <b>Why this exists.</b> <see cref="DomainEvent.OccurredAt"/> and <see cref="DomainEvent.EventId"/>
/// are <c>init</c> properties, so whoever writes <c>new OrderPlaced(...)</c> can set them. But an
/// aggregate raises its own events, so the only code that could pass an initialiser is the aggregate,
/// and a test that calls <c>order.Cancel(reason)</c> never sees the constructor. Without a seam the
/// timestamp on an event raised through the domain is whatever the wall clock said.
/// </para>
/// <para>
/// <b>Why it is scoped and not a static setter.</b> A <c>public static TimeProvider Clock { get; set; }</c>
/// would be simpler to write and impossible to use safely: test frameworks run test classes in
/// parallel by default, so one test's clock becomes another test's clock, and forgetting to restore
/// it leaks into everything that runs afterwards. The value here is held in an
/// <see cref="AsyncLocal{T}"/> and installed for a scope, so it reaches the code the scope calls and
/// nothing else.
/// </para>
/// <para>
/// <b>What it does not cover.</b> The value flows to whatever the scope calls, including awaited
/// work. It does not reach a thread or a background service that was already running when the scope
/// was entered, because that work captured its execution context earlier. Events raised there keep
/// the system clock.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var clock = new ManualClock(new DateTimeOffset(2024, 1, 21, 17, 34, 7, TimeSpan.Zero));
/// using (DomainEventClock.Use(clock))
/// {
///     var order = new Order(orderId, customerId);
///     order.DomainEvents[0].OccurredAt.Should().Be(clock.GetUtcNow());
/// }
/// </code>
/// </example>
public static class DomainEventClock
{
    private static readonly AsyncLocal<Scope?> Ambient = new();

    /// <summary>
    /// The clock in force for the current asynchronous flow, or <see cref="TimeProvider.System"/>
    /// when no scope is active.
    /// </summary>
    public static TimeProvider Current => Ambient.Value?.TimeProvider ?? System.TimeProvider.System;

    /// <summary>The current UTC time according to <see cref="Current"/>.</summary>
    public static DateTimeOffset UtcNow => Current.GetUtcNow();

    /// <summary>
    /// Produces the identifier for a new event. By default this is a version 7 <see cref="Guid"/>
    /// whose timestamp comes from <see cref="Current"/>, so a fixed clock fixes the ordering half of
    /// the id while the random half keeps it unique. A scope may replace it outright to get a fully
    /// predictable id.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The active clock reports a time before 1970-01-01 UTC, which a version 7 <see cref="Guid"/>
    /// cannot encode. Start a fake clock at a realistic date.
    /// </exception>
    public static Guid NewEventId()
    {
        if (Ambient.Value is not { } scope)
        {
            return Guid.CreateVersion7();
        }

        var now = scope.TimeProvider.GetUtcNow();
        return scope.EventIds is { } factory ? factory(now) : Guid.CreateVersion7(now);
    }

    /// <summary>
    /// Installs <paramref name="timeProvider"/> as the clock for the current asynchronous flow until
    /// the returned scope is disposed. Scopes nest: disposing restores whatever was in force before.
    /// <para>
    /// Intended for tests and for replaying recorded events. Production code has no reason to call it:
    /// an event that reports a time other than the time it happened is a lie told to every consumer
    /// downstream.
    /// </para>
    /// </summary>
    /// <param name="timeProvider">The clock <see cref="DomainEvent.OccurredAt"/> reads.</param>
    /// <param name="eventIds">
    /// Optional replacement for <see cref="NewEventId"/>, called with the time the clock reports.
    /// It must still return a distinct value per call: the event id is the idempotency key handlers
    /// deduplicate on, and it is the primary key of the outbox table, so a repeated value makes the
    /// save fail.
    /// </param>
    /// <returns>A scope that restores the previous clock when disposed.</returns>
    public static IDisposable Use(TimeProvider timeProvider, Func<DateTimeOffset, Guid>? eventIds = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var previous = Ambient.Value;
        Ambient.Value = new Scope(timeProvider, eventIds);
        return new Restore(previous);
    }

    private sealed record Scope(TimeProvider TimeProvider, Func<DateTimeOffset, Guid>? EventIds);

    private sealed class Restore(Scope? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Ambient.Value = previous;
        }
    }
}

/// <summary>
/// Resolves the stable name of a domain event type: the name <see cref="DomainEventNameAttribute"/> pins,
/// or otherwise the conventional one, its module and its class name in kebab case
/// (<c>ordering.order-placed</c>). A trailing <c>V</c> and a number is the version, not part of the name.
/// </summary>
public static class DomainEventName
{
    private static readonly ConcurrentDictionary<Type, string> Names = new();

    /// <summary>The name declared by <see cref="DomainEventNameAttribute"/>, otherwise the conventional one (<see cref="ConventionalNameOf"/>).</summary>
    public static string Of(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        return Names.GetOrAdd(eventType, static type =>
            ((DomainEventNameAttribute?)Attribute.GetCustomAttribute(type, typeof(DomainEventNameAttribute), inherit: false))?.Name
            ?? ConventionalNameOf(type));
    }

    /// <summary>
    /// The name the convention gives <paramref name="eventType"/>, whatever its attributes say: the module its
    /// assembly declares with <c>[assembly: Module]</c> and its class name without a version suffix, both in
    /// kebab case. <c>OrderPlacedV2</c> in module <c>Ordering</c> is <c>ordering.order-placed</c>; outside a
    /// module it is <c>order-placed</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="eventType"/> is null.</exception>
    public static string ConventionalNameOf(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        var module = eventType.Assembly.GetCustomAttribute<ModuleAttribute>()?.Name.Trim();
        return EventNameConvention.NameFor(eventType.Name, string.IsNullOrEmpty(module) ? null : module);
    }

    /// <summary>
    /// The version a trailing <c>V</c> and a number in the class name gives, <c>2</c> for <c>OrderPlacedV2</c>,
    /// or null when the name has no such suffix.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="eventType"/> is null.</exception>
    public static int? VersionSuffixOf(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return EventNameConvention.Split(eventType.Name).Version;
    }

    /// <summary>The stable name of <typeparamref name="TEvent"/>.</summary>
    public static string Of<TEvent>() where TEvent : IDomainEvent => Of(typeof(TEvent));

    /// <summary>The stable name of the runtime type of <paramref name="domainEvent"/>.</summary>
    public static string Of(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return Of(domainEvent.GetType());
    }
}
