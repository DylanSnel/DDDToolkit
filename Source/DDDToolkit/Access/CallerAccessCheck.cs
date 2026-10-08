using DDDToolkit.Abstractions.Access;
using DDDToolkit.Exceptions;

namespace DDDToolkit.Access;

/// <summary>
/// Decides the core's requirements about who is calling: <see cref="AccessRequirement.SignedIn"/> and
/// <see cref="AccessRequirement.RequiresSystemWork"/>. It asks the host who is calling, and nothing else, so a
/// host without any supporting domain has both.
/// </summary>
/// <remarks>
/// <c>AddAccessChecks</c> puts it first in every module's set, before the checks the module adds: the core's
/// cases mean the same in every module, and no check of a module that says yes to more than it should can take
/// them over. A set made by hand, in a test, is given it the same way:
/// <code>
/// var checks = new AccessChecks&lt;IBillingRequest&gt;([new CallerAccessCheck(callers), new BillingAccessCheck(...)]);
/// </code>
/// <para>
/// System work is what trusted code began on purpose: <c>Callers.Begin(Caller.System)</c> or
/// <c>Callers.Begin(Caller.SystemIn(scope))</c> around seeding, an import or a job, or a supporting domain's own
/// way to begin it, such as Tenancy's <c>TenancyWork</c>. The host's accessor must answer system work, and the
/// caller begun for the flow must be system work too. A flow nobody began a caller for is not, even where the
/// accessor answers <see cref="Caller.System"/> for it, as an <see cref="AmbientCallerAccessor"/> does in a host
/// that does not require explicit callers: in such a host that answer is also what a web request gets when no
/// accessor knows requests, and a request only the application may send must not pass on a default.
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
    public bool Decides(AccessRequirement requirement) => requirement is AccessRequirement.SignedInUser or AccessRequirement.SystemWork;

    /// <inheritdoc />
    /// <exception cref="RefusalException">
    /// <see cref="ToolkitRefusals.NotSignedIn"/> for a caller that is no signed-in user where one is required;
    /// <see cref="ToolkitRefusals.SystemOnly"/> for a caller that is not the application itself where only it may send the request.
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

    /// <summary>Whether <paramref name="caller"/> is the application's own work: <see cref="Caller.System"/>, or a scoped <see cref="Caller.SystemIn"/>.</summary>
    private static bool IsSystemWork(Caller? caller) => caller?.Kind is CallerKind.System or CallerKind.SystemIn;

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

            case AccessRequirement.SystemWork:
                // The application itself, above the policies or confined to a scope: both are system work, and no
                // user is either. Only system work that trusted code began counts. A host that does not require
                // explicit callers answers the application itself for any flow nobody began a caller for, a web
                // request its accessor does not know included, and that is no proof that the application sent it.
                // The host is asked first, so a host that does require them still fails such work instead.
                if (!IsSystemWork(callers.Current) || !IsSystemWork(Callers.Ambient))
                {
                    throw ToolkitRefusals.Refuse(ToolkitRefusals.SystemOnly);
                }

                return;

            default:
                throw new InvalidOperationException(
                    $"{WrittenTypeNames.Of(request.GetType())} declares '{WrittenTypeNames.Of(requirement.GetType())}', which the core's check of who is calling does not decide. "
                    + "A requirement nothing checks lets nobody through.");
        }
    }
}
