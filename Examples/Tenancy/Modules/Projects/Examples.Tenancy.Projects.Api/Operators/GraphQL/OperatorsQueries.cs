using Examples.Tenancy.Projects.Application.Operators;
using Examples.Tenancy.Projects.Application.Operators.Queries;
using HotChocolate;
using Mediator;

namespace Examples.Tenancy.Projects.Api.Operators.GraphQL;

/// <summary>
/// What the application's own staff ask about the projects. The field sends the query its route sends, and
/// nothing else.
/// </summary>
/// <remarks>
/// An operator holds no seat, so the field takes its tenant as an argument, where the route has it in its path,
/// and the host lets it through for an operator instead of a seat. The query refuses whoever is no operator
/// itself, wherever it is sent from.
/// </remarks>
internal static class OperatorsQueries
{
    /// <summary>
    /// Every project of one tenant, by number, each with who changed it last: what
    /// <c>GET /operations/tenants/{tenant}/projects</c> answers. A tenant that does not exist has none.
    /// </summary>
    [Query]
    public static async Task<IReadOnlyList<TenantProject>> GetTenantProjectsAsync(TenantId tenant, [Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new TenantProjects(tenant), cancellationToken);
}
