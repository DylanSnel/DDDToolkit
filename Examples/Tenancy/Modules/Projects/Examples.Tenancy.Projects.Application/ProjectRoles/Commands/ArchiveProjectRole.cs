using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Access;
using Mediator;

namespace Examples.Tenancy.Projects.Application.ProjectRoles.Commands;

/// <summary>
/// Archives one of the tenant's project roles: it gives nothing from then on and goes to nobody, and stays on the
/// crews that hold it, so what they held still has a name. The crew lead's role, which every owner holds, is not
/// archived.
/// </summary>
/// <param name="Id">The role.</param>
public sealed record ArchiveProjectRole(ProjectRoleId Id) : ICommand, IProjectsRequest
{
    /// <summary>The key the caller holds for the whole tenant.</summary>
    public const string RequiredKey = ProjectRoleListing.KeysKey;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.ForTheWholeTenant(RequiredKey);
}

/// <summary>Handles <see cref="ArchiveProjectRole"/>: loads the role, archives it and saves.</summary>
/// <remarks>
/// Nothing of a package's stands between this command and the role, and nothing else asks who may send it, so the
/// handler asks again what its request declared, of the very checks the pipeline asks. It then decides the same
/// wherever it runs, in a program that left the module's access behavior out of the pipeline as much as in the
/// host, for the check's statement a second time on a command that is sent rarely.
/// </remarks>
/// <param name="store">Where project roles are loaded and saved.</param>
/// <param name="membership">The projects' rules, which name the crew lead's role.</param>
/// <param name="checks">The module's access checks, which hold a request to what it declared.</param>
public sealed class ArchiveProjectRoleHandler(IProjectStore store, ProjectMembership membership, AccessChecks<IProjectsRequest> checks) : ICommandHandler<ArchiveProjectRole>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c>, naming the key, for a caller that does not hold it for the whole tenant;
    /// <c>projects.role-not-found</c>, for a role of another tenant too; <c>projects.role-archived</c>,
    /// <c>projects.owner-role-stays</c>.
    /// </exception>
    public async ValueTask<Unit> Handle(ArchiveProjectRole command, CancellationToken cancellationToken)
    {
        await checks.RequireAsync(command, cancellationToken);

        var role = await store.LoadRoleAsync(command.Id, cancellationToken) ?? throw ProjectRefusals.Refuse(ProjectRefusals.RoleNotFound);

        role.TakeOutOfUse(membership.Rules);
        await store.SaveAsync(cancellationToken);
        return Unit.Value;
    }
}
