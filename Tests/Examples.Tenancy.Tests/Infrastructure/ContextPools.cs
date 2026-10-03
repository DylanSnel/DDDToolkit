using Microsoft.EntityFrameworkCore;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>What a test asks of a context pool, through its factory.</summary>
/// <remarks>
/// A pool hands out its idle contexts in the order they came back, and makes a new one when none is idle. So taking
/// contexts and keeping each reaches every idle one in turn. The outbox pollers take a context from the same pool
/// every second, for a moment: while one of them holds the context a test waits for, the pool makes a new instance
/// for whoever asks, which a first rental shows, and the test waits a little and asks again.
/// <para>
/// The same first rental says the pool is empty: a context a poller gave back after the one a test looks for sits
/// behind it, idle, and is taken too before the test says that only one is left.
/// </para>
/// </remarks>
public static class ContextPools
{
    /// <summary>How long a pool is given to hand a context out again.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Whether <paramref name="factory"/> hands out <paramref name="given"/>, a context somebody gave back to its
    /// pool. Everything taken to find out goes back when it is done.
    /// </summary>
    /// <typeparam name="TContext">The pooled context.</typeparam>
    /// <param name="factory">The pool's factory.</param>
    /// <param name="given">The instance that was given back.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    public static async Task<bool> HandsOutAgainAsync<TContext>(this IDbContextFactory<TContext> factory, DbContext given, CancellationToken cancellationToken)
        where TContext : DbContext
    {
        await using var taken = await TakeUntilAsync(factory, given, untilThePoolIsEmpty: false, cancellationToken);
        return taken.Found;
    }

    /// <summary>
    /// Leaves <paramref name="given"/> as the only idle context of its pool, so that it is the one the pool hands
    /// out next: every other idle context is taken and kept until what this returns is disposed.
    /// </summary>
    /// <typeparam name="TContext">The pooled context.</typeparam>
    /// <param name="factory">The pool's factory.</param>
    /// <param name="given">The instance that was given back, and is to be handed out next.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <exception cref="InvalidOperationException">The pool did not hand <paramref name="given"/> out in time.</exception>
    public static async Task<IAsyncDisposable> LeaveOnlyAsync<TContext>(this IDbContextFactory<TContext> factory, DbContext given, CancellationToken cancellationToken)
        where TContext : DbContext
    {
        var taken = await TakeUntilAsync(factory, given, untilThePoolIsEmpty: true, cancellationToken);
        if (!taken.Found)
        {
            await taken.DisposeAsync();
            throw new InvalidOperationException("The pool of " + typeof(TContext).Name + " did not hand the context out again.");
        }

        await taken.GiveBackTheOneFoundAsync();
        return taken;
    }

    /// <summary>
    /// Takes contexts from the pool, keeping each, until it is handed <paramref name="given"/> or runs out of
    /// patience; and, when <paramref name="untilThePoolIsEmpty"/>, goes on after that until the pool has no idle
    /// context left, which it shows by making a new one.
    /// </summary>
    private static async Task<Taken<TContext>> TakeUntilAsync<TContext>(IDbContextFactory<TContext> factory, DbContext given, bool untilThePoolIsEmpty, CancellationToken cancellationToken)
        where TContext : DbContext
    {
        var taken = new Taken<TContext>();
        var deadline = DateTimeOffset.UtcNow + Patience;
        try
        {
            while (DateTimeOffset.UtcNow < deadline)
            {
                var next = await factory.CreateDbContextAsync(cancellationToken);
                taken.Add(next, found: ReferenceEquals(next, given));

                // A first rental is an instance the pool just made: none was idle.
                var madeNow = next.ContextId.Lease == 1;
                if (taken.Found && (madeNow || !untilThePoolIsEmpty))
                {
                    break;
                }

                // So somebody holds the one waited for.
                if (!taken.Found && madeNow)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
                }
            }

            return taken;
        }
        catch
        {
            await taken.DisposeAsync();
            throw;
        }
    }

    /// <summary>The contexts a test took from a pool, which go back to it when this is disposed.</summary>
    private sealed class Taken<TContext> : IAsyncDisposable
        where TContext : DbContext
    {
        private readonly List<TContext> _held = [];
        private TContext? _found;

        /// <summary>Whether the pool handed out the context that was looked for.</summary>
        public bool Found { get; private set; }

        public void Add(TContext context, bool found)
        {
            if (found)
            {
                Found = true;
                _found = context;
            }
            else
            {
                _held.Add(context);
            }
        }

        /// <summary>Gives the context that was looked for back, alone: the others stay taken.</summary>
        public async ValueTask GiveBackTheOneFoundAsync()
        {
            // Once only: given back, the instance is the next renter's, and disposing it again would end that rental.
            if (Interlocked.Exchange(ref _found, null) is { } found)
            {
                await found.DisposeAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await GiveBackTheOneFoundAsync();
            foreach (var context in _held)
            {
                await context.DisposeAsync();
            }

            _held.Clear();
        }
    }
}
