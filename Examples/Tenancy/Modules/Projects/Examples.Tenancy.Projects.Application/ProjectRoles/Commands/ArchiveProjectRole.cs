using Mediator;

namespace Examples.Tenancy.Projects.Application.ProjectRoles.Commands;

/// <summary>
/// Archives one of the tenant's project roles: it gives nothing from then on and goes to nobody, and stays on the
/// crews that hold it, so what they held still has a name. Any role is archived alike, the crew lead's too: a
/// project's owner holds none.
/// </summary>
/// <param name="Id">The role.</param>
[RequiresPermission(RequiredPermission)]
public sealed record ArchiveProjectRole(ProjectRoleId Id) : ICommand, IProjectsRequest
{
    /// <summary>The permission the caller holds for the whole tenant.</summary>
    public const string RequiredPermission = ProjectRoleListing.ManageRolesPermission;
}

/// <summary>Handles <see cref="ArchiveProjectRole"/> with one call to the project role manager, which archives the role and saves.</summary>
/// <remarks>
/// The manager asks again who may: the permission the projects' rules name for their roles, held for the whole tenant,
/// which the join with Tenancy answers. So it decides the same wherever the command runs, in a program that left the
/// module's access behavior out of the pipeline as much as in the host.
/// </remarks>
/// <param name="roles">The project role manager, which the Membership package's generator writes for <c>ProjectRole</c>.</param>
public sealed class ArchiveProjectRoleHandler(ProjectRoleManager roles) : ICommandHandler<ArchiveProjectRole>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c>, naming the permission, for a caller that does not hold it for the whole tenant;
    /// <c>projects.role-not-found</c>, for a role of another tenant too; <c>projects.role-archived</c>.
    /// </exception>
    public async ValueTask<Unit> Handle(ArchiveProjectRole command, CancellationToken cancellationToken)
    {
        await roles.ArchiveAsync(command.Id, cancellationToken);
        return Unit.Value;
    }
}
