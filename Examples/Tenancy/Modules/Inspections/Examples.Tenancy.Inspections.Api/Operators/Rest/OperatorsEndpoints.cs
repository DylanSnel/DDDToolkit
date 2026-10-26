using Examples.Tenancy.Inspections.Application.Operators.Queries;
using Examples.Tenancy.Tenants.Contracts.ValueObjects;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Inspections.Api.Operators.Rest;

/// <summary>
/// What the application's own staff read of the inspections, over HTTP: every inspection of one project of one
/// tenant. The route sends the query of the application project's <c>Operators</c> feature.
/// </summary>
/// <remarks>
/// An operator holds no seat, so the route names its tenant in the path and not in a header. It reads; there is no
/// route here that records an inspection, because an operator changes nothing itself.
/// <para>
/// Like every route of this project, it decides nothing and takes only the sender;
/// <see cref="InspectionsModule"/> says what all of them keep to. Internal: the module's entry maps it into the
/// group the host hands it.
/// </para>
/// </remarks>
internal static class OperatorsEndpoints
{
    /// <summary>
    /// Maps <c>GET /operations/tenants/{tenant}/projects/{project}/inspections</c> into <paramref name="group"/>,
    /// the host's group for operators.
    /// </summary>
    public static IEndpointRouteBuilder MapOperatorsEndpoints(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // Newest first, each with the seat it names as its recorder and who the save that wrote it ran as.
        group.MapGet("/operations/tenants/{tenant}/projects/{project}/inspections", async (TenantId tenant, ProjectId project, ISender sender, CancellationToken cancellationToken)
            => Results.Ok((await sender.Send(new TenantProjectInspections(tenant, project), cancellationToken)).Select(item => new
            {
                item.Id,
                item.Title,
                from = item.Days.From,
                until = item.Days.Until,
                item.RecordedBy,
                item.RecordedAt,
                changedBy = new { kind = item.ChangedBy.Kind, seatId = item.ChangedBy.Seat },
            })));

        return group;
    }
}
