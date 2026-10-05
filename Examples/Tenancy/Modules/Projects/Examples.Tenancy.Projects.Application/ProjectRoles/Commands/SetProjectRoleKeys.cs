using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Projects.Application.ProjectRoles.Commands;

/// <summary>
/// Has one of the tenant's project roles give these keys from now on, on every crew it is held on: every key it is
/// to give, chosen from the keys a crew role may give.
/// </summary>
/// <remarks>
/// The crew lead's role is re-keyed like any other. What a project's owner may do does not hang on it: the owner
/// holds every key of a lead by owning the project.
/// </remarks>
/// <param name="Id">The role.</param>
/// <param name="Keys">Every key it is to give.</param>
public sealed record SetProjectRoleKeys(ProjectRoleId Id, IReadOnlyCollection<string> Keys) : ICommand, IProjectsRequest
{
    /// <summary>The key the caller holds for the whole tenant.</summary>
    public const string RequiredKey = ProjectRoleListing.KeysKey;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.ForTheWholeTenant(RequiredKey);
}

/// <summary>Handles <see cref="SetProjectRoleKeys"/>: loads the role, sets its keys and saves.</summary>
/// <remarks>
/// Nothing of a package's stands between this command and the role, and nothing else asks who may send it, so the
/// handler asks again what its request declared, of the very checks the pipeline asks. It then decides the same
/// wherever it runs, in a program that left the module's access behavior out of the pipeline as much as in the
/// host, for the check's statement a second time on a command that is sent rarely.
/// </remarks>
/// <param name="store">Where project roles are loaded and saved.</param>
/// <param name="membership">The projects' rules, which say what a crew role may give.</param>
/// <param name="catalogue">The permission keys the application knows.</param>
/// <param name="checks">The module's access checks, which hold a request to what it declared.</param>
public sealed class SetProjectRoleKeysHandler(IProjectStore store, ProjectMembership membership, TenancyCatalogue catalogue, AccessChecks<IProjectsRequest> checks) : ICommandHandler<SetProjectRoleKeys>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c>, naming the key, for a caller that does not hold it for the whole tenant;
    /// <c>tenancy.unknown-permission</c>; <c>projects.role-not-found</c>, for a role of another tenant too;
    /// <c>projects.role-archived</c>, <c>projects.key-not-for-members</c>.
    /// </exception>
    public async ValueTask<Unit> Handle(SetProjectRoleKeys command, CancellationToken cancellationToken)
    {
        await checks.RequireAsync(command, cancellationToken);

        var keys = MakeProjectRole.Known(command.Keys, catalogue);
        var role = await store.LoadRoleAsync(command.Id, cancellationToken) ?? throw ProjectRefusals.Of(ProjectRefusals.RoleNotFound);

        role.SetRoleKeys(keys, membership.Rules);
        await store.SaveAsync(cancellationToken);
        return Unit.Value;
    }
}
