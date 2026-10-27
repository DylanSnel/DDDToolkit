using DDDToolkit.Abstractions.Access;
using DDDToolkit.Exceptions;

namespace DDDToolkit.Access;

/// <summary>
/// Decides the core's requirements about who is calling: <see cref="AccessRequirement.SignedIn"/>;
/// <see cref="AccessRequirement.Own{TValue}"/>, whether a value the request names is the caller's own; and
/// <see cref="AccessRequirement.OwnerOf{TId}"/>, whether the caller owns the aggregate the request names. It asks the
/// host who is calling, a handle what the caller's own value of a type is, and, for the owner of an aggregate, the
/// storage's reader of that owner, and nothing else, so a host without any supporting domain has it.
/// </summary>
/// <remarks>
/// <c>AddAccessChecks</c> puts it first in every module's set, before the checks the module adds: the core's
/// cases mean the same in every module, and no check of a module that says yes to more than it should can take
/// them over. A set made by hand, in a test, is given it the same way:
/// <code>
/// var checks = new AccessChecks&lt;IBillingRequest&gt;([new CallerAccessCheck(callers), new BillingAccessCheck(...)]);
/// </code>
/// <para>
/// What the caller's own value of a type is, the <see cref="ICallersOwn{TValue}"/> registered for the type says, found
/// in the scope's services it is given; for a <see cref="Guid"/> it is the caller's verified identity unless one is
/// registered. A value of a type nothing says that for is a case it does not decide, so the set of checks stops the
/// request, as it stops any requirement nothing decides, and names the registration that is missing.
/// </para>
/// <para>
/// The owner of an aggregate is read by the <see cref="IAggregateOwners{TId}"/> registered for the aggregate's id, which
/// the Entity Framework package registers for every id. Where none is, the case is not decided either, and the request is
/// stopped the same way. Where one is, the caller's own of the owner's type is asked first, and a caller who has none is
/// refused with <see cref="ToolkitRefusals.NotOwn"/> before anything is read; then the owner is read, one column of one
/// row, as the caller: a row there is none of for the caller is <see cref="ToolkitRefusals.NotFound"/>, and a row
/// that is somebody else's, or nobody's, <see cref="ToolkitRefusals.NotOwn"/>.
/// </para>
/// <para>
/// The application's own work is no case of it. System work is a scope trusted code begins around the work,
/// <c>Callers.Begin(Caller.System)</c> or a supporting domain's own way such as Tenancy's
/// <c>access.BeginSystem()</c>, and a request it sends declares who may send it like any other: what only
/// system work may do, a supporting domain's use case refuses everyone else, with
/// <see cref="ToolkitRefusals.SystemOnly"/>. System work is nobody's sign-in, and has no value of its own, so it is
/// refused here as any caller who did not sign in is.
/// </para>
/// <para>
/// It keeps nothing, and reads nothing of its own: the owner of an aggregate is the one thing read, and the storage
/// reads it. A host that requires every flow of work to say who it runs as
/// (<see cref="CallerOptions.RequireExplicitCallers"/>) gets <see cref="NoCallerException"/> from its accessor
/// for work nobody began a caller for, and that is not caught here: such work is a mistake to fix, not a
/// caller to refuse. The set of checks asks it who is calling before any check, so that holds for every request of
/// the set, whatever it requires.
/// </para>
/// </remarks>
/// <param name="callers">Who is calling, as the host says it.</param>
/// <param name="services">
/// The services of the scope the check runs in, where an <see cref="ICallersOwn{TValue}"/> and an
/// <see cref="IAggregateOwners{TId}"/> are registered; left out, only a <see cref="Guid"/> is said the caller's own of, and
/// the owner of no aggregate is read.
/// </param>
public sealed class CallerAccessCheck(ICallerAccessor callers, IServiceProvider? services = null) : IAccessCheck
{
    /// <inheritdoc />
    public bool Decides(AccessRequirement requirement)
        => requirement is AccessRequirement.SignedInUser
           || (requirement is IOwnedRequirement owned && owned.CanBeAnswered(services))
           || (requirement is IOwnedAggregateRequirement aggregate && aggregate.CanBeAnswered(services));

    /// <inheritdoc />
    /// <exception cref="RefusalException">
    /// <see cref="ToolkitRefusals.NotSignedIn"/> for a caller that is no signed-in user where one is required,
    /// <see cref="ToolkitRefusals.NotOwn"/> for one the request's value is not the own of, or the aggregate it names not
    /// the caller's, and <see cref="ToolkitRefusals.NotFound"/> for an aggregate there is no row of for the caller.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The requirement is not one of this check's cases, or nothing says what the caller's own of an aggregate's owner is.
    /// </exception>
    /// <exception cref="NoCallerException">Nobody is calling, and the host requires every flow of work to say who it runs as.</exception>
    public ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(request);

        // The owner of an aggregate is the one case that reads, and what reads it is the storage's. Whatever fails before
        // the reading comes as the task's too.
        if (requirement is IOwnedAggregateRequirement aggregate)
        {
            try
            {
                return aggregate.RequireAsync(
                    callers.Current,
                    services ?? throw new InvalidOperationException(
                        $"{WrittenTypeNames.Of(request.GetType())} declares '{WrittenTypeNames.Of(requirement.GetType())}', and the core's check of who is calling was made without the scope's services, where what reads an aggregate's owner is registered. "
                        + "A requirement nothing checks lets nobody through."),
                    cancellationToken);
            }
            catch (Exception failed)
            {
                return ValueTask.FromException(failed);
            }
        }

        // Nothing is read, so nothing is awaited; a refusal still comes as the task's, as it does from a check
        // that reads, so whoever awaits the set sees one way of being refused.
        try
        {
            Require(requirement, request);
            return ValueTask.CompletedTask;
        }
        catch (Exception refused)
        {
            return ValueTask.FromException(refused);
        }
    }

    /// <summary>
    /// Asks the host who is calling, and nothing else: what <see cref="AccessChecks{TRequests}"/> does once before it
    /// asks any check, so that in a host that requires every flow of work to say who it runs as, work nobody began a
    /// caller for fails with <see cref="NoCallerException"/> whatever its request requires, a case of a supporting
    /// domain's that asks that domain's own caller included.
    /// </summary>
    /// <exception cref="NoCallerException">Nobody is calling, and the host requires every flow of work to say who it runs as.</exception>
    internal void AskWhoIsCalling() => _ = callers.Current;

    /// <summary>Returns when the current caller is who <paramref name="requirement"/> lets through, and throws otherwise.</summary>
    private void Require(AccessRequirement requirement, IRequireAccess request)
    {
        switch (requirement)
        {
            case AccessRequirement.SignedInUser:
                if (!callers.Current.IsSignedIn)
                {
                    throw ToolkitRefusals.Refuse(ToolkitRefusals.NotSignedIn);
                }

                return;

            case IOwnedRequirement owned:
                // Compared at the door with what the caller has: the request names the value, and never says whose it is.
                if (!owned.IsTheCallers(callers.Current, services))
                {
                    throw ToolkitRefusals.Refuse(ToolkitRefusals.NotOwn);
                }

                return;

            default:
                throw new InvalidOperationException(
                    $"{WrittenTypeNames.Of(request.GetType())} declares '{WrittenTypeNames.Of(requirement.GetType())}', which the core's check of who is calling does not decide. "
                    + "A requirement nothing checks lets nobody through.");
        }
    }
}
