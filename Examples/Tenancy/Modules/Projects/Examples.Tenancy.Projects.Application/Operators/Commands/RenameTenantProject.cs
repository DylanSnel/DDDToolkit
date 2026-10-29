using DDDToolkit.Supporting.Tenancy.Access;
using Mediator;

namespace Examples.Tenancy.Projects.Application.Operators.Commands;

/// <summary>
/// Renames a project of one tenant for the application's own staff, as support does when a customer asks.
/// </summary>
/// <remarks>
/// For operators only. An operator holds no seat, so the command names the tenant it acts in, and runs as operator work
/// there, in this module's scope, recorded as the operator. A request that names another tenant beside it is refused.
/// The work holds every permission on the tenant's projects, so the handler does the operator's act and nothing more.
/// </remarks>
/// <param name="Tenant">The tenant whose project is renamed.</param>
/// <param name="Project">The project.</param>
/// <param name="Name">Its new name.</param>
[Operator(Scope = ProjectMembership.Scope)]
public sealed record RenameTenantProject([ForTenant] TenantId Tenant, ProjectId Project, string Name) : ICommand, IProjectsRequest;

/// <summary>Handles <see cref="RenameTenantProject"/>: loads the project its command names, renames it and saves.</summary>
/// <param name="store">Where projects are loaded and saved.</param>
public sealed class RenameTenantProjectHandler(IProjectStore store) : ICommandHandler<RenameTenantProject>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException"><c>projects.not-found</c>, <c>projects.closed</c>, <c>projects.name-invalid</c>.</exception>
    public async ValueTask<Unit> Handle(RenameTenantProject command, CancellationToken cancellationToken)
    {
        var project = await store.LoadAsync(command.Project, expectedVersion: null, cancellationToken)
            ?? throw ProjectRefusals.Refuse(ProjectRefusals.NotFound);

        project.Rename(command.Name);
        await store.SaveAsync(cancellationToken);
        return Unit.Value;
    }
}
