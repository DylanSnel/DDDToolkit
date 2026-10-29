using Examples.Tenancy.Projects.Application.Operators;
using Examples.Tenancy.Projects.Application.Operators.Commands;
using Examples.Tenancy.Projects.Application.Operators.Queries;
using HotChocolate;
using HotChocolate.Types.Relay;
using Mediator;

namespace Examples.Tenancy.Projects.Api.Operators.GraphQL;

/// <summary>
/// What the application's own staff change of the projects. The mutation sends the command its route sends, then reads
/// the project as the operators' query answers it; a refusal arrives in the payload's <c>errors</c>.
/// </summary>
/// <remarks>
/// An operator holds no seat, so the field takes its tenant as an argument, where the route has it in its path, and the
/// host lets it through for an operator instead of a seat. The command runs as operator work in that tenant.
/// </remarks>
internal static class OperatorsMutations
{
    /// <summary>
    /// Renames one of a tenant's projects, as support does when a customer asks: what
    /// <c>PUT /operations/tenants/{tenant}/projects/{project}/name</c> does. The project is recorded as changed by an
    /// operator.
    /// </summary>
    [Mutation]
    public static async Task<TenantProject?> TenantProjectRenameAsync(
        TenantId tenant,
        [ID("Project")] ProjectId project,
        string name,
        [Service] ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(new RenameTenantProject(tenant, project, name), cancellationToken);
        return (await sender.Send(new TenantProjects(tenant), cancellationToken)).FirstOrDefault(each => each.Id == project);
    }
}
