using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DDDToolkit.EntityFramework.Interceptors;

/// <summary>
/// Runs the invariants of everything a save writes, at the consistency boundary. Before every
/// <c>SaveChanges</c> it asks each entity the save adds or modifies, child entities included, and the
/// aggregate root of each changed child, for its own invariants, so a root whose only change is to
/// something it owns answers as well. Anything that fails throws an
/// <see cref="InvariantViolationException"/> and nothing is written.
/// <para>
/// The transaction is the honest place for this. An aggregate is allowed to be inconsistent halfway
/// through a method, and a check at every call site is a check you will forget; a check at the
/// commit is the promise the aggregate actually makes, which is that it is never stored broken.
/// </para>
/// <para>
/// The persistence path enumerates where the domain walks, and that difference is deliberate. Ask an
/// aggregate in memory <c>EnsureInvariants()</c> and it answers for the whole aggregate, walking into
/// the children it holds, because a handler that just acted on a root is asking whether the boundary
/// it acted on is still intact. A save cannot ask that question. It deals in partial graphs, and the
/// change tracker is the only thing that knows which children were loaded and which of them changed,
/// so the save asks what is tracked and changed, and asks each of them <c>EnsureOwnInvariants()</c>.
/// Two questions, two mechanisms. The self-only half is what keeps the count honest as well:
/// <see cref="EntitiesToCheck"/> lists a changed child and its root separately, so walking from here
/// would report every child violation twice, once from the child and once through its root.
/// </para>
/// <para>
/// The two agree in the direction that matters, because the domain walk is the stricter of the two:
/// it covers children this save never changed, so an aggregate that satisfies the walk satisfies the
/// save. The reverse does not hold, which is exactly why the walk is the one a handler asks and this
/// one is the one that refuses to write.
/// </para>
/// <para>
/// Which objects the save asks is therefore decided from the change tracker alone, never by reading a
/// navigation property. The tracker knows only what was loaded and what changed, so a child that was
/// never loaded is never asked and never quietly fetched; see <see cref="EntitiesToCheck"/> for what
/// that buys.
/// </para>
/// <para>
/// An entity that implements no <c>CheckInvariants()</c> seam costs nothing here beyond the empty
/// call: the compiler erased its body. Register the interceptor after
/// <see cref="PublishDomainEventsInterceptor"/>, so it sees whatever the event handlers changed, and
/// before <see cref="AggregateVersionInterceptor"/>, so a save this interceptor rejects leaves no
/// version bumped on the aggregate in memory. <c>UseDDDToolkit</c> does both for you.
/// </para>
/// </summary>
public sealed class InvariantInterceptor : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            CheckInvariants(context);
        }

        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            CheckInvariants(context);
        }

        return ValueTask.FromResult(result);
    }

    /// <summary>
    /// Asks everything this context is about to write for its own invariants: every entity it adds or
    /// modifies and the aggregate root of every changed child, each asked once. Public so a unit of
    /// work of your own can make the same check at a point of its choosing.
    /// </summary>
    /// <exception cref="InvariantViolationException">One of those objects is inconsistent.</exception>
    public static void CheckInvariants(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var entity in EntitiesToCheck.Find(context))
        {
            entity.EnsureOwnInvariants();
        }
    }

    /// <summary>
    /// Asks the same question as <see cref="CheckInvariants"/> and answers with a list instead of an
    /// exception: every violation this context would raise if it saved now, over the whole unit of
    /// work. Empty means the save would pass. This is the stage before the save, where "not yet
    /// consistent" is an answer the application handles rather than an exception it catches.
    /// <para>
    /// Violations arrive in the order the objects are checked, roots before the children they own,
    /// each appearing once because every object answers for itself and no root repeats what its
    /// children already said, and each carries the <see cref="InvariantViolation.Code"/> its rule was
    /// written with, so a caller can branch on a rule without matching on text.
    /// </para>
    /// </summary>
    /// <param name="context">The unit of work to ask. It is not saved and nothing is written.</param>
    public static IReadOnlyList<InvariantViolation> GetInvariantViolations(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Allocated only once something is actually broken: this runs on a context that is usually fine.
        List<InvariantViolation>? violations = null;

        foreach (var entity in EntitiesToCheck.Find(context))
        {
            var broken = entity.GetOwnInvariantViolations();
            if (broken.Count == 0)
            {
                continue;
            }

            violations ??= [];
            violations.AddRange(broken);
        }

        return violations is null ? [] : violations;
    }
}
