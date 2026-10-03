using Examples.Tenancy.Inspections.Application.Operators.Queries;
using Examples.Tenancy.Inspections.Application.Recording;
using Examples.Tenancy.Tenants.Contracts.ValueObjects;
using HotChocolate;
using HotChocolate.Types.Relay;
using Mediator;

namespace Examples.Tenancy.Inspections.Api.Operators.GraphQL;

/// <summary>
/// What the application's own staff ask about the inspections. The field sends the query its route sends, and
/// nothing else.
/// </summary>
/// <remarks>
/// The one root field of this schema a client asks, and it is not for a seat: a seat asks a project for its
/// inspections. An operator holds no seat and sees no project through Projects' gate, so the field takes the
/// tenant and the project as arguments, where the route has them in its path, and the host lets it through for
/// an operator instead of a seat. The query refuses whoever is no operator itself, wherever it is sent from.
/// </remarks>
internal static class OperatorsQueries
{
    /// <summary>
    /// Every inspection of one project of one tenant, newest first, each with the seat that recorded it and who
    /// the save that wrote it ran as: what <c>GET /operations/tenants/{tenant}/projects/{project}/inspections</c>
    /// answers. A project that is not that tenant's has none there.
    /// </summary>
    /// <remarks>
    /// The seat is its id for an operator: what a seat is called is Tenancy's to say, inside a tenant.
    /// </remarks>
    [Query]
    public static async Task<IReadOnlyList<InspectionOverview>> GetTenantProjectInspectionsAsync(
        TenantId tenant,
        [ID("Project")] ProjectId project,
        [Service] ISender sender,
        CancellationToken cancellationToken)
        => await sender.Send(new TenantProjectInspections(tenant, project), cancellationToken);
}
