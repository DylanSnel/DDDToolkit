namespace DDDToolkit.Access;

/// <summary>
/// The request a flow of work is handling: the one whose access checks that flow asked last, once they let it
/// through. Code that runs after the checks in the same flow, the handler and the save it ends with, is not
/// always handed the request, and asks here which one it serves.
/// </summary>
/// <remarks>
/// <see cref="AccessChecks{TRequests}.RequireAsync"/> puts the request in hand as it starts to ask, and the request
/// counts from the moment the checks let it through: one they refused is in nobody's hand. It follows the flow the
/// way an <see cref="AsyncLocal{T}"/> does: into whatever the method that asked the checks runs after them, the
/// handler and its save among it, into tasks started there, and not back out of that method. So whatever sends a
/// request asks the checks and runs the handler in one <see langword="async"/> method, as the behavior the
/// generator writes does:
/// <code>
/// await checks.RequireAsync(request, cancellationToken);      // the request is in hand from here
/// await handler.HandleAsync(request, cancellationToken);      // and here, down to the save
/// </code>
/// A helper that asks the checks in a method of its own has the request in hand there alone, and hands nothing
/// back: the handler it calls next runs with no request in hand. And only an <see langword="async"/> method gives
/// its caller the flow back as it was: one that is not, that asks the checks and returns the task of the handling
/// it chains on, leaves the request in hand for its caller, and whatever that caller runs next, a handler it calls
/// directly included, is taken for part of the request's handling. A save that runs after the method returned,
/// in a behavior around it or in an endpoint after it sent the request, has nothing in hand.
/// <para>
/// A request is in hand for one handling. Sending it again asks the checks again, and that pass is what is in hand
/// from then on. A request sent from inside the handling of another, through a sender whose behavior asks its
/// checks in a method of its own, is in hand for its own handling, and the other one is in hand again once it
/// returns. A query answered with a stream has its request in hand until its handler hands out the first item:
/// every item after that is asked for by whoever reads the stream, in that reader's flow.
/// </para>
/// <para>
/// What a check keeps for the request it let through (<see cref="Checked{T}.KeepFor"/>) is kept with the request
/// in hand as well, so code that runs in the handling finds it without the request
/// (<see cref="Checked{T}.TryFindInHand"/>): how a package checks at the save what its check read before the
/// handler.
/// </para>
/// </remarks>
public static class RequestInHand
{
    private static readonly AsyncLocal<Hand?> Ambient = new();

    /// <summary>
    /// The request whose access checks this flow of work asked last and that they let through, or
    /// <see langword="null"/>: outside the handling of any request, as in background work or a handler called
    /// directly, and in the handling of one the checks refused.
    /// </summary>
    public static object? Current => Ambient.Value is { Passed: true } hand ? hand.Request : null;

    /// <summary>The hand of the request that is in hand now, once its checks let it through.</summary>
    internal static Hand? Passed => Ambient.Value is { Passed: true } hand ? hand : null;

    /// <summary>
    /// Puts <paramref name="request"/> in hand for the rest of this flow of work, as it is about to be checked.
    /// Synchronous on purpose: a value set in an asynchronous method stays in that method, and this one has to
    /// reach the caller's, which runs the handler next.
    /// </summary>
    /// <param name="request">The very request the checks are asked about.</param>
    internal static Hand Take(object request)
    {
        var hand = new Hand(request);
        Ambient.Value = hand;
        return hand;
    }

    /// <summary>The hand <paramref name="request"/> is in, in this flow, while its checks run or after: <see langword="null"/> for any other request.</summary>
    /// <param name="request">A request.</param>
    internal static Hand? Of(object request) => Ambient.Value is { } hand && ReferenceEquals(hand.Request, request) ? hand : null;

    /// <summary>
    /// One request in hand: whether its checks let it through yet, and what they kept for it, by the type it was
    /// kept as. One object per pass, shared by every task the flow starts, so it is safe to use from several.
    /// </summary>
    /// <param name="request">The request.</param>
    internal sealed class Hand(object request)
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<Type, object> _kept = [];
        private volatile bool _passed;

        /// <summary>The request.</summary>
        public object Request { get; } = request;

        /// <summary>Whether the checks let the request through.</summary>
        public bool Passed => _passed;

        /// <summary>Marks the request as let through.</summary>
        public void Pass() => _passed = true;

        /// <summary>Marks the request as let through once <paramref name="checking"/> completes without a refusal, and returns what the caller awaits.</summary>
        /// <param name="checking">The check that holds the request to its requirement.</param>
        public ValueTask PassWhen(ValueTask checking)
        {
            if (checking.IsCompletedSuccessfully)
            {
                checking.GetAwaiter().GetResult();
                Pass();
                return ValueTask.CompletedTask;
            }

            return PassAfterAsync(checking);
        }

        /// <summary>Keeps <paramref name="value"/> as what was kept of <typeparamref name="T"/>, in the place of an earlier one.</summary>
        public void Keep<T>(T value)
            where T : notnull
        {
            lock (_gate)
            {
                _kept[typeof(T)] = value;
            }
        }

        /// <summary>What was kept of <typeparamref name="T"/>, left where it is.</summary>
        public bool TryFind<T>(out T value)
            where T : notnull
        {
            lock (_gate)
            {
                if (_kept.TryGetValue(typeof(T), out var kept))
                {
                    value = (T)kept;
                    return true;
                }
            }

            value = default!;
            return false;
        }

        /// <summary>
        /// Awaits the check, then marks the request as let through. Asynchronous, and only the object changes:
        /// the flow that asked holds the same object, so it sees the mark.
        /// </summary>
        private async ValueTask PassAfterAsync(ValueTask checking)
        {
            await checking.ConfigureAwait(false);
            Pass();
        }
    }
}
