using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Membership.UseCases;
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
/// and makes none of them until that role is renamed. A tenant without the crew lead's role cannot open a
/// project: there is no role to give its owner (<c>projects.no-lead-role</c>).
/// <para>
/// Who may send it is a caller in the tenant, <c>[InTenant]</c>, the module's default, which it takes by saying nothing
/// of its own: the one requirement that lets the system work that sends it through. Nothing requires system work at
/// the door, since system work is a scope the sender begins around the send, <c>TenancyUseCases.BeginSystemIn(tenant)</c>,
/// as the seeding does. What the work then may do is the handler's to say, and it says only system work in the tenant
/// sets a tenant up: a seat that sends it gets past the door and is refused there, <c>access.system-only</c>.
/// </para>
/// </remarks>
/// <param name="Ids">
/// The ids of the roles to make, by the key of the starter role each is made from, for seeding and imports whose
/// ids are known beforehand; the rest get new ones.
/// </param>
public sealed record SetUpProjectRoles(IReadOnlyDictionary<string, ProjectRoleId>? Ids = null) : ICommand, IProjectsRequest;

/// <summary>Handles <see cref="SetUpProjectRoles"/>: reads the tenant's roles, makes the starter roles it lacks and saves.</summary>
/// <remarks>
/// Nothing of a package's stands between this command and the role, so the handler asks again what its request
/// declared, of the very checks the pipeline asks, and then narrower: system work in the very tenant it sets up,
/// which is where the roles are made, and nobody else. It then decides the same wherever it runs, in a program that
/// left the module's access behavior out of the pipeline as much as in the host.
/// </remarks>
/// <param name="store">Where project roles are read, added and saved.</param>
/// <param name="membership">The projects' rules and starter roles.</param>
/// <param name="answers">Tenancy's answers about the current caller: the tenant being set up.</param>
/// <param name="checks">The module's access checks, which hold a request to what it declared.</param>
public sealed class SetUpProjectRolesHandler(IProjectStore store, ProjectMembership membership, SampleAnswers answers, AccessChecks<IProjectsRequest> checks) : ICommandHandler<SetUpProjectRoles>
{
    /// <inheritdoc />
    /// <exception cref="RefusalException"><c>access.system-only</c>: anybody but system work in the tenant.</exception>
    public async ValueTask<Unit> Handle(SetUpProjectRoles command, CancellationToken cancellationToken)
    {
        await checks.RequireAsync(command, cancellationToken);

        var scope = answers.RequireTenant();
        if (!scope.BySystem)
        {
            throw ToolkitRefusals.Refuse(ToolkitRefusals.SystemOnly);
        }

        var tenant = scope.Tenant;
        var missing = StarterRoles.Missing(membership.Rules, await store.RolesOfTheTenantAsync(cancellationToken));
        if (missing.Count == 0)
        {
            return Unit.Value;
        }

        foreach (var draft in missing)
        {
            var starter = membership.StarterRoles.Single(role => role.Key == draft.MadeFrom);
            var id = command.Ids is { } ids && ids.TryGetValue(starter.Key, out var given) ? given : ProjectRoleId.Create();
            store.Add(new ProjectRole(id, tenant, draft with { Name = starter.Name, Description = starter.Description }, membership.Rules));
        }

        await store.SaveAsync(cancellationToken);
        return Unit.Value;
    }
}
