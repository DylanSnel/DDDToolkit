using Mediator;

namespace Examples.Tenancy.Projects.Application.ProjectRoles.Commands;

/// <summary>
/// Gives one of the tenant's project roles another name, or another description, or both: both as they are to be.
/// A starter role is renamed like any other, and is found by what it was made from all the same.
/// </summary>
/// <param name="Id">The role.</param>
/// <param name="Name">Its name, as it is to be.</param>
/// <param name="Description">What it is for, as it is to be; <see langword="null"/> clears it.</param>
[RequiresPermission(RequiredPermission)]
public sealed record RenameProjectRole(ProjectRoleId Id, string Name, string? Description) : ICommand, IProjectsRequest
{
    /// <summary>The permission the caller holds for the whole tenant.</summary>
    public const string RequiredPermission = ProjectRoleListing.ManageRolesPermission;
}

/// <summary>Handles <see cref="RenameProjectRole"/> with one call to the project role manager, which renames the role and saves.</summary>
/// <remarks>
/// The manager asks again who may: the permission the projects' rules name for their roles, held for the whole tenant,
/// which the join with Tenancy answers.
/// </remarks>
/// <param name="roles">The project role manager.</param>
public sealed class RenameProjectRoleHandler(ProjectRoleManager roles) : ICommandHandler<RenameProjectRole>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c>, naming the permission, for a caller that does not hold it for the whole tenant;
    /// <c>projects.role-not-found</c>, for a role of another tenant too; <c>projects.role-archived</c>,
    /// <c>projects.role-name-invalid</c>, <c>projects.role-name-taken</c>.
    /// </exception>
    public async ValueTask<Unit> Handle(RenameProjectRole command, CancellationToken cancellationToken)
    {
        await roles.RenameAsync(command.Id, command.Name, command.Description, cancellationToken);
        return Unit.Value;
    }
}
