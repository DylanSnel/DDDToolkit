using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Access;
using Mediator;

namespace Examples.Tenancy.Projects.Application.ProjectRoles.Commands;

/// <summary>
/// Gives one of the tenant's project roles another name, or another description, or both: both as they are to be.
/// A starter role is renamed like any other, and is found by what it was made from all the same.
/// </summary>
/// <param name="Id">The role.</param>
/// <param name="Name">Its name, as it is to be.</param>
/// <param name="Description">What it is for, as it is to be; <see langword="null"/> clears it.</param>
public sealed record RenameProjectRole(ProjectRoleId Id, string Name, string? Description) : ICommand, IProjectsRequest
{
    /// <summary>The key the caller holds for the whole tenant.</summary>
    public const string RequiredKey = ProjectRoleListing.KeysKey;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new TenancyRequirement.ForTheWholeTenant(RequiredKey);
}

/// <summary>Handles <see cref="RenameProjectRole"/>: loads the role, renames it and saves.</summary>
/// <remarks>
/// Nothing of a package's stands between this command and the role, and nothing else asks who may send it, so the
/// handler asks again what its request declared, of the very checks the pipeline asks. It then decides the same
/// wherever it runs, in a program that left the module's access behavior out of the pipeline as much as in the
/// host, for the check's statement a second time on a command that is sent rarely.
/// </remarks>
/// <param name="store">Where project roles are loaded and saved.</param>
/// <param name="membership">The projects' rules.</param>
/// <param name="checks">The module's access checks, which hold a request to what it declared.</param>
public sealed class RenameProjectRoleHandler(IProjectStore store, ProjectMembership membership, AccessChecks<IProjectsRequest> checks) : ICommandHandler<RenameProjectRole>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c>, naming the key, for a caller that does not hold it for the whole tenant;
    /// <c>projects.role-not-found</c>, for a role of another tenant too; <c>projects.role-archived</c>,
    /// <c>projects.role-name-invalid</c>, <c>projects.role-name-taken</c>.
    /// </exception>
    public async ValueTask<Unit> Handle(RenameProjectRole command, CancellationToken cancellationToken)
    {
        await checks.RequireAsync(command, cancellationToken);

        var role = await store.LoadRoleAsync(command.Id, cancellationToken) ?? throw ProjectRefusals.Of(ProjectRefusals.RoleNotFound);

        role.RenameRole(command.Name, command.Description, membership.Rules);
        await store.SaveAsync(cancellationToken);
        return Unit.Value;
    }
}
