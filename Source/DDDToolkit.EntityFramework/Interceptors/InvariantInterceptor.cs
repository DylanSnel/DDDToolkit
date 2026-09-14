using DDDToolkit.Exceptions;
using DDDToolkit.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DDDToolkit.EntityFramework.Interceptors;

/// <summary>
/// Runs each aggregate's own invariants at the consistency boundary. Before every
/// <c>SaveChanges</c> it calls <c>EnsureInvariants()</c> on every aggregate root the save adds or
/// modifies, including a root whose only change is to something it owns. A root that fails throws an
/// <see cref="InvariantViolationException"/> and nothing is written.
/// <para>
/// The transaction is the honest place for this. An aggregate is allowed to be inconsistent halfway
/// through a method, and a check at every call site is a check you will forget; a check at the
/// commit is the promise the aggregate actually makes, which is that it is never stored broken.
/// </para>
/// <para>
/// An aggregate that implements no <c>CheckInvariants()</c> seam costs nothing here beyond the empty
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
    /// Runs <c>EnsureInvariants()</c> on every aggregate root this context is about to write. Public
    /// so a unit of work of your own can make the same check at a point of its choosing.
    /// </summary>
    /// <exception cref="InvariantViolationException">One of those roots is inconsistent.</exception>
    public static void CheckInvariants(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var root in ChangedAggregateRoots.Find(context))
        {
            if (root.Entity is IHasInvariants hasInvariants)
            {
                hasInvariants.EnsureInvariants();
            }
        }
    }
}
