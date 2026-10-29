using DDDToolkit.Exceptions;
using Mediator;

namespace Examples.Tenancy.Projects.Application.ProjectRoles.Commands;

/// <summary>
/// Sets a tenant up for projects: makes the starter project roles it does not have yet, each once, with the name
/// and description the application gives it. Sent by whatever sets a tenant up, as system work in that tenant,
/// right after the tenant is provisioned.
/// </summary>
/// <remarks>
/// A starter role is told by what it was made from, not by its name or its status, so one the tenant renamed or
/// archived is not made again, and sending this twice makes nothing the second time. Two sent at the same moment
/// make no role twice: the unique index over the tenant and what a role was made from refuses the second save,
/// which then fails as a whole, and sending it again makes nothing. The roles are made in one save, so a starter
/// role whose name the tenant already gave a role of its own refuses the set-up, <c>projects.role-name-taken</c>,
/// and makes none of them until that role is renamed. A tenant opens projects without any: a project's owner holds
/// no role.
/// <para>
/// Who may send it is a caller in the tenant, <c>[InTenant]</c>, the module's default, which it takes by saying nothing
/// of its own: the one requirement that lets the system work that sends it through. Nothing requires system work at
/// the door, since system work is a scope the sender begins around the send, <c>access.BeginSystemIn(tenant)</c>,
/// as the seeding does. What the work then may do is the handler's to say, and it says only system work in the tenant
/// sets a tenant up: a seat that sends it gets past the door and is refused there, <c>access.system-only</c>.
/// </para>
/// </remarks>
/// <param name="Ids">
/// The ids of the roles to make, by the permission of the starter role each is made from, for seeding and imports whose
/// ids are known beforehand; the rest get new ones.
/// </param>
public sealed record SetUpProjectRoles(IReadOnlyDictionary<string, ProjectRoleId>? Ids = null) : ICommand, IProjectsRequest;

/// <summary>
/// Handles <see cref="SetUpProjectRoles"/>: only system work in the tenant sets it up; the project role manager makes the
/// starter roles the tenant lacks, each with the name and description the module gives it, and saves them.
/// </summary>
/// <param name="roles">The project role manager, which makes a starter role once, told by what it was made from.</param>
/// <param name="membership">The projects' starter roles, with their names and descriptions.</param>
/// <param name="tenancy">Tenancy, asked about the current caller: the tenant being set up.</param>
public sealed class SetUpProjectRolesHandler(ProjectRoleManager roles, ProjectMembership membership, TenancyAccessManager tenancy) : ICommandHandler<SetUpProjectRoles>
{
    /// <inheritdoc />
    /// <exception cref="RefusalException"><c>access.system-only</c>: anybody but system work in the tenant.</exception>
    public async ValueTask<Unit> Handle(SetUpProjectRoles command, CancellationToken cancellationToken)
    {
        var scope = tenancy.CurrentTenant.Require();
        if (!scope.BySystem)
        {
            throw ToolkitRefusals.Refuse(ToolkitRefusals.SystemOnly);
        }

        await roles.CreateStarterRolesAsync(
            draft =>
            {
                var starter = membership.StarterRoles.Single(role => role.Key == draft.MadeFrom);
                var id = command.Ids is { } ids && ids.TryGetValue(starter.Key, out var given) ? given : ProjectRoleId.Create();
                return new ProjectRole(id, scope.Id, draft with { Name = starter.Name, Description = starter.Description }, roles.Rules);
            },
            cancellationToken);
        return Unit.Value;
    }
}
