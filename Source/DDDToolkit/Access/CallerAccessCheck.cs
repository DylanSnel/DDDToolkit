using DDDToolkit.Abstractions.Access;
using DDDToolkit.Exceptions;

namespace DDDToolkit.Access;

/// <summary>
/// Decides the core's requirement about who is calling, <see cref="AccessRequirement.SignedIn"/>. It asks the host
/// who is calling, and nothing else, so a host without any supporting domain has it.
/// </summary>
/// <remarks>
/// <c>AddAccessChecks</c> puts it first in every module's set, before the checks the module adds: the core's
/// cases mean the same in every module, and no check of a module that says yes to more than it should can take
/// them over. A set made by hand, in a test, is given it the same way:
/// <code>
/// var checks = new AccessChecks&lt;IBillingRequest&gt;([new CallerAccessCheck(callers), new BillingAccessCheck(...)]);
/// </code>
/// <para>
/// The application's own work is no case of it. System work is a scope trusted code begins around the work,
/// <c>Callers.Begin(Caller.System)</c> or a supporting domain's own way such as Tenancy's
/// <c>TenancyUseCases.BeginSystem()</c>, and a request it sends declares who may send it like any other: what only
/// system work may do, a supporting domain's use case refuses everyone else, with
/// <see cref="ToolkitRefusals.SystemOnly"/>. System work is nobody's sign-in, so it is refused here as any caller
/// who did not sign in is.
/// </para>
/// <para>
/// It reads nothing and keeps nothing. A host that requires every flow of work to say who it runs as
/// (<see cref="CallerOptions.RequireExplicitCallers"/>) gets <see cref="NoCallerException"/> from its accessor
/// for work nobody began a caller for, and that is not caught here: such work is a mistake to fix, not a
/// caller to refuse.
/// </para>
/// </remarks>
/// <param name="callers">Who is calling, as the host says it.</param>
public sealed class CallerAccessCheck(ICallerAccessor callers) : IAccessCheck
{
    /// <inheritdoc />
    public bool Decides(AccessRequirement requirement) => requirement is AccessRequirement.SignedInUser;

    /// <inheritdoc />
    /// <exception cref="RefusalException">
    /// <see cref="ToolkitRefusals.NotSignedIn"/> for a caller that is no signed-in user where one is required.
    /// </exception>
    /// <exception cref="InvalidOperationException">The requirement is not one of this check's cases.</exception>
    /// <exception cref="NoCallerException">Nobody is calling, and the host requires every flow of work to say who it runs as.</exception>
    public ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(request);

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

            default:
                throw new InvalidOperationException(
                    $"{WrittenTypeNames.Of(request.GetType())} declares '{WrittenTypeNames.Of(requirement.GetType())}', which the core's check of who is calling does not decide. "
                    + "A requirement nothing checks lets nobody through.");
        }
    }
}
