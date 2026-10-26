using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace DDDToolkit.Access;

/// <summary>
/// What an access check read on the way, kept for the code after it that handles the same request: the tie
/// between what was checked and what is done.
/// </summary>
/// <remarks>
/// The check runs before the handler and reads what the request is about: the thing itself, the version it is
/// at, an answer it had to work out to decide. Most handlers need none of it: they act on what their request
/// names, which is exactly what the check was asked about. A handler that does need it, an answer it would
/// otherwise ask for a second time, takes it from here:
/// <code>
/// var answer = checkedAnswers.TakeFor(query);
/// </code>
/// Code that runs in the request's handling and is not handed the request, such as an interceptor of the save
/// the handler ends with, finds what was kept through the request in hand instead (<see cref="TryFindInHand(out T)"/>,
/// <see cref="RequestInHand"/>), and leaves it where it is. That is how a package holds a save to what its check
/// read, with nothing written in the handler.
/// <para>
/// Taking it also closes the way round the check. A handler reached without its request having passed the
/// check, called directly rather than sent, has nothing to take, and <see cref="TakeFor"/> throws. What was kept
/// is handed out once: the same request handed to a handler once more has nothing to take either. A request its
/// checks refused has nothing kept for it, a requirement that combines cases included
/// (<see cref="AccessRequirement.All"/>, <see cref="AccessRequirement.Any"/>): what the check of one case read is
/// kept only once the whole requirement let the caller through, and only for the cases it was let through by.
/// </para>
/// <para>
/// A requirement that combines cases may have several of them keep a <typeparamref name="T"/>, two keys on two
/// documents say: then each is kept, in the order the cases were asked. <see cref="TakeFor"/> hands out one and
/// refuses to guess among several, <see cref="TakeAllFor"/> hands out all of them, and
/// <see cref="TryFindInHand(Func{T, bool}, out T)"/> finds the one code of the handling is about.
/// </para>
/// <para>
/// What is kept for a request is what its latest pass read. A request that passed, never reached its
/// handler because something in between failed, and is sent again in the same scope, is handed what the
/// second pass read: the version it is at now, not the one from before, and nothing of the first pass stays
/// behind for a later call to find. So one request object is one handling at a time. Sent twice side by
/// side, the two share what the later pass kept, and only one of them takes it: send two requests.
/// </para>
/// <para>
/// One per scope and per <typeparamref name="T"/>, and a scope's requests may run side by side, so what is
/// kept is kept per request, in a table that is safe to use from several at once. A request is found by
/// reference, not by value: two equal commands sent in one scope are two requests, each with its own check.
/// Nothing holds a request here once it is done with.
/// </para>
/// </remarks>
/// <typeparam name="T">What the check hands the handler: the id it checked, or a record of what it read.</typeparam>
public sealed class Checked<T>
    where T : notnull
{
    private readonly ConditionalWeakTable<object, Kept> _kept = new();

    /// <summary>
    /// What the access check kept for <paramref name="request"/> when the request last passed it. Handed out
    /// once.
    /// </summary>
    /// <param name="request">The very request the handler was handed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Nothing was kept for <paramref name="request"/>, or what was kept was taken before: the request did not
    /// pass a check that keeps a <typeparamref name="T"/> on its way here, so its handler has nothing to act on. Or
    /// several were kept, by the cases of a requirement that combines them, and which one the handler means is not this
    /// method's to guess: <see cref="TakeAllFor"/> hands out all of them.
    /// </exception>
    public T TakeFor(object request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Taken away as it is handed out, so of two that come for a request that passed only the first gets it.
        var values = Take(request, onlyOne: true, out var count);
        return count switch
        {
            0 => throw NothingKept(request),
            1 => values[0],
            _ => throw new InvalidOperationException(
                $"{count} of {WrittenTypeNames.Of(typeof(T))} were kept for this {WrittenTypeNames.Of(request.GetType())}: its requirement combines cases whose checks each kept one, "
                + "one for each resource it names say, and which one the handler acts on is not for this to guess. Take them all with TakeAllFor, and find the one by what it is about."),
        };
    }

    /// <summary>
    /// Everything the access checks kept for <paramref name="request"/> when the request last passed them, in the order
    /// its cases were asked: one for each case of a combination whose check keeps a <typeparamref name="T"/>. Handed
    /// out once.
    /// </summary>
    /// <param name="request">The very request the handler was handed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Nothing was kept for <paramref name="request"/>, or what was kept was taken before, as for <see cref="TakeFor"/>.
    /// </exception>
    public IReadOnlyList<T> TakeAllFor(object request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var values = Take(request, onlyOne: false, out var count);
        return count == 0 ? throw NothingKept(request) : values;
    }

    /// <summary>
    /// Keeps <paramref name="value"/> for the handler of <paramref name="request"/>, in the place of whatever
    /// an earlier pass of the same request kept and no handler took. Only the check that read it calls this,
    /// once it has let the caller through. A check asked again after its request's handler ran
    /// (<see cref="RequestInHand.StillPassesAsync"/>) keeps nothing: the handler took what it acts on, and
    /// nothing of that second asking stays behind for a later call to find.
    /// </summary>
    /// <remarks>
    /// The check of a case of a combined requirement keeps what it read beside what the other cases' checks keep in the
    /// same pass, and only once the whole requirement let the caller through: of <see cref="AccessRequirement.All"/>
    /// every case's, of <see cref="AccessRequirement.Any"/> the one case's that let it through. Until then it is held
    /// back, so a combination that refuses the caller leaves nothing behind, whichever of its cases had passed.
    /// </remarks>
    /// <param name="request">The very request being checked.</param>
    /// <param name="value">What the check read.</param>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> or <paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="request"/> is a struct. A request is found again by reference, and a struct is another
    /// copy each time it is handed on, so its handler would never find what was kept.
    /// </exception>
    public void KeepFor(object request, T value)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(value);

        if (request.GetType().IsValueType)
        {
            throw new ArgumentException(
                $"{WrittenTypeNames.Of(request.GetType())} is a struct, and what a check keeps is found again by the request's reference. Declare the request as a class or a record class.",
                nameof(request));
        }

        if (RequestInHand.IsAskingAgain)
        {
            return;
        }

        // With the request in hand as well, while its checks run in this flow, for code of its handling that is
        // not handed the request. What is kept for a request outside any checks, by a test say, is in no hand.
        var hand = RequestInHand.Of(request);
        if (RequestInHand.HeldBack is { } pending)
        {
            // A case of a combination: kept once the combination let the caller through by it, and dropped otherwise.
            pending.Add(() => Keep(request, value, hand));
            return;
        }

        Keep(request, value, hand);
    }

    /// <summary>
    /// What a check kept of <typeparamref name="T"/> for the request in hand (<see cref="RequestInHand.Current"/>),
    /// left where it is: for code that runs in that request's handling and is not handed the request, such as an
    /// interceptor of the save its handler ends with. <see cref="TakeFor"/> does not take it from here: this is
    /// what the flow's request passed with, for as long as it is in hand. Where its checks kept several, this is the
    /// first they kept; <see cref="TryFindInHand(Func{T, bool}, out T)"/> finds the one that is about something.
    /// </summary>
    /// <param name="value">What was kept, when this returns <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="false"/> outside the handling of a request its checks let through in this flow, and where
    /// they kept no <typeparamref name="T"/> for it.
    /// </returns>
    public static bool TryFindInHand([MaybeNullWhen(false)] out T value) => TryFindInHand(static _ => true, out value);

    /// <summary>
    /// The first of what the checks kept of <typeparamref name="T"/> for the request in hand that
    /// <paramref name="match"/> takes, left where it is: the hold on the very resource a save changes, of a request whose
    /// checks read two.
    /// </summary>
    /// <param name="match">Which of what was kept is the one asked for.</param>
    /// <param name="value">What was kept, when this returns <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="false"/> outside the handling of a request its checks let through in this flow, and where
    /// they kept no <typeparamref name="T"/> for it that <paramref name="match"/> takes.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="match"/> is null.</exception>
    public static bool TryFindInHand(Func<T, bool> match, [MaybeNullWhen(false)] out T value)
    {
        ArgumentNullException.ThrowIfNull(match);

        if (RequestInHand.Current is { } hand && hand.TryFind(match, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Everything the checks kept of <typeparamref name="T"/> for the request in hand, in the order its cases were asked,
    /// left where it is; empty outside the handling of a request its checks let through in this flow.
    /// </summary>
    public static IReadOnlyList<T> FindAllInHand() => RequestInHand.Current is { } hand ? hand.FindAll<T>() : [];

    /// <summary>Keeps <paramref name="value"/> for <paramref name="request"/>, and with the hand it is in, now.</summary>
    private void Keep(object request, T value, RequestInHand? hand)
    {
        _kept.GetOrCreateValue(request).Keep(value, hand);
        hand?.Keep(value);
    }

    /// <summary>Takes what was kept for <paramref name="request"/>: all of it, or, with <paramref name="onlyOne"/>, only where one was.</summary>
    private T[] Take(object request, bool onlyOne, out int count)
    {
        if (_kept.TryGetValue(request, out var kept))
        {
            return kept.Take(onlyOne, out count);
        }

        count = 0;
        return [];
    }

    private static InvalidOperationException NothingKept(object request)
        => new(
            $"No {WrittenTypeNames.Of(typeof(T))} was kept for this {WrittenTypeNames.Of(request.GetType())}. A handler acts on what its request's access check read, "
            + $"so the request must pass its access checks on the way to the handler, once for each time it is handled, and must declare a requirement whose check keeps a {WrittenTypeNames.Of(typeof(T))}.");

    /// <summary>What the latest pass of one request kept, until its handler takes it.</summary>
    private sealed class Kept
    {
        private readonly Lock _gate = new();
        private readonly List<T> _values = [];

        /// <summary>The pass the values were kept in: the hand the request was in, or none outside any checks.</summary>
        private object? _pass;

        /// <summary>
        /// Keeps <paramref name="value"/> beside what the same pass kept, and in the place of what an earlier pass kept
        /// and nobody took. Outside any checks every keep is a pass of its own.
        /// </summary>
        public void Keep(T value, object? pass)
        {
            lock (_gate)
            {
                if (pass is null || !ReferenceEquals(pass, _pass))
                {
                    _values.Clear();
                    _pass = pass;
                }

                _values.Add(value);
            }
        }

        /// <summary>Hands out what was kept, once: all of it, or, with <paramref name="onlyOne"/>, nothing where there are several.</summary>
        public T[] Take(bool onlyOne, out int count)
        {
            lock (_gate)
            {
                count = _values.Count;
                if (count == 0 || (onlyOne && count > 1))
                {
                    return [];
                }

                T[] values = [.. _values];
                _values.Clear();
                _pass = null;
                return values;
            }
        }
    }
}
