using System.Collections.Concurrent;
using System.Data.Common;
using DDDToolkit.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// Makes two reads that were started together really run at the same time, and records the contexts they ran on.
/// </summary>
/// <remarks>
/// Two queries started with <c>Task.WhenAll</c> may still run one after the other, the first answered before the
/// second is sent, and then prove nothing about sharing a context. Added to a context's options, this holds the
/// first statement of a flow it watches until a second one arrives, as two resolvers of one GraphQL request meet
/// when the database takes its time. On one context that is the second operation Entity Framework refuses; on a
/// context each, both go on.
/// <para>
/// It watches one flow of work, the one that called <see cref="WatchThisFlow"/>, and whatever is awaited from it.
/// The outbox pollers and other tests send statements too, on flows of their own, and are let through untouched.
/// </para>
/// <para>
/// The contexts come from a pool, so one instance serves one read after another: a read that starts when another
/// is done may be handed the instance that one gave back. What tells two reads apart is the rental, not the
/// instance: <see cref="DbContextId"/> names the instance and how many times it has been taken from the pool.
/// Two reads that meet hold a context each at that moment, so they are two instances as well.
/// </para>
/// </remarks>
public sealed class SideBySide : DbCommandInterceptor
{
    private readonly AsyncLocal<Encounter?> _flow = new();

    /// <summary>
    /// Watches this flow of work from now on: its first statement waits for a second. Returns what to ask
    /// afterwards.
    /// </summary>
    public Encounter WatchThisFlow()
    {
        var encounter = new Encounter();
        _flow.Value = encounter;
        return encounter;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (_flow.Value is { } encounter)
        {
            await encounter.ArriveAsync(eventData.Context, cancellationToken);
        }

        return result;
    }

    /// <summary>What happened in one watched flow.</summary>
    public sealed class Encounter
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentQueue<DbContext> _contexts = new();
        private readonly ConcurrentQueue<DbContextId> _rentals = new();
        private int _notPooled;
        private readonly Lock _turn = new();
        private int _arrived;
        private bool _waiting;
        private bool _met;

        /// <summary>Whether a second statement arrived while the first was held: whether two reads ran at the same time.</summary>
        public bool Met
        {
            get
            {
                lock (_turn)
                {
                    return _met;
                }
            }
        }

        /// <summary>Every context instance that sent a statement in the flow, once each.</summary>
        public IReadOnlyList<DbContext> Contexts => [.. _contexts.Distinct()];

        /// <summary>
        /// Every rental of a context that sent a statement in the flow, once each: an instance taken from the pool,
        /// given back and taken again is two.
        /// </summary>
        public IReadOnlyList<DbContextId> Rentals => [.. _rentals.Distinct()];

        /// <summary>Whether every context that sent a statement in the flow came from a pool.</summary>
        public bool AllPooled => Volatile.Read(ref _notPooled) == 0;

        /// <summary>Lets a held statement go on although no second one came, for a test in which the second was refused.</summary>
        public void Release() => _gate.TrySetResult();

        internal async Task ArriveAsync(DbContext? context, CancellationToken cancellationToken)
        {
            if (context is not null)
            {
                // Read now, while the context is rented: given back, it answers nothing about itself.
                _contexts.Enqueue(context);
                _rentals.Enqueue(context.ContextId);
                if (!context.IsPooled())
                {
                    Interlocked.Increment(ref _notPooled);
                }
            }

            // Who is first, and that it waits, is settled in one step. Two reads may arrive from two threads at the
            // same instant; decided in two steps, the second could look before the first had said it waits, and
            // the first would then wait for nobody.
            bool first;
            lock (_turn)
            {
                first = ++_arrived == 1;
                if (first)
                {
                    _waiting = true;
                }
                else if (_waiting)
                {
                    // Only a statement of another read can arrive while the first is held: its own next one cannot.
                    _met = true;
                    _gate.TrySetResult();
                }
            }

            if (first)
            {
                // The first waits for the second. When none comes it goes on alone, and the test sees they never met.
                await Task.WhenAny(_gate.Task, Task.Delay(Patience, cancellationToken));
                lock (_turn)
                {
                    _waiting = false;
                }
            }
        }
    }
}
