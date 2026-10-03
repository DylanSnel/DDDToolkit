using DDDToolkit.Exceptions;

namespace Examples.Tenancy.Projects.Application.Access;

/// <summary>
/// Decides the requirement that is Projects' own (<see cref="ProjectsRequirement"/>) before a handler of this
/// module runs: registered for <see cref="IProjectsRequest"/>, next to the Membership package's check for a key on
/// a project and Tenancy's for the cases that are Tenancy's, and asked by the module's access behavior.
/// </summary>
/// <remarks>
/// The caller's own refusal when it is nobody comes first, then <c>projects.not-permitted</c> for a key it does not
/// hold at the unit. Only then does the handler run. The unit the request passed for it keeps per request
/// (<see cref="Checked{T}"/>), so a handler acts on exactly the unit that was checked.
/// <para>
/// It fails closed: a case added to <see cref="ProjectsRequirement"/> without its branch here stops every request
/// that declares it, rather than letting it through.
/// </para>
/// </remarks>
/// <param name="access">Who holds which key where.</param>
/// <param name="checkedUnit">Where the unit a request was checked at is kept for its handler.</param>
public sealed class ProjectsAccessCheck(ProjectAccess access, Checked<OrganizationUnitId> checkedUnit) : IAccessCheck
{
    /// <inheritdoc />
    public bool Decides(AccessRequirement requirement) => requirement is ProjectsRequirement;

    /// <inheritdoc />
    /// <exception cref="RefusalException">The caller is not who the request requires.</exception>
    /// <exception cref="InvalidOperationException">
    /// The requirement is one this check has no branch for; or the caller is system work outside any tenant.
    /// </exception>
    public async ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(request);

        switch (requirement)
        {
            case ProjectsRequirement.AtUnit required:
                // Kept under the request itself, so its handler acts at the very unit that was checked.
                await access.RequireAtAsync(required.Unit, required.Key, cancellationToken);
                checkedUnit.KeepFor(request, required.Unit);
                break;

            default:
                throw new InvalidOperationException(
                    $"{request.GetType().Name} declares '{requirement}', which the Projects module's access check has no branch for. "
                    + "A requirement nothing checks lets nobody through: give it a branch in ProjectsAccessCheck.");
        }
    }
}
