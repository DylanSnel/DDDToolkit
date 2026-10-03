using Examples.Tenancy.Projects.Application.Operators.Queries;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Projects.Api.Operators.Rest;

/// <summary>
/// What the application's own staff read of the projects, over HTTP: every project of one tenant. The route sends
/// the query of the application project's <c>Operators</c> feature.
/// </summary>
/// <remarks>
/// An operator holds no seat, so the route names its tenant in the path and not in a header. It reads; there is no
/// route here that changes a project, because an operator changes nothing itself.
/// <para>
/// Like every route of this project, it decides nothing and takes only the sender;
/// <see cref="ProjectsModule"/> says what all of them keep to. Internal: the module's entry maps it into the
/// group the host hands it.
/// </para>
/// </remarks>
internal static class OperatorsEndpoints
{
    /// <summary>Maps <c>GET /operations/tenants/{tenant}/projects</c> into <paramref name="group"/>, the host's group for operators.</summary>
    public static IEndpointRouteBuilder MapOperatorsEndpoints(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // By number, each with who changed it last. Ids and the project's own texts, as every answer of this module.
        group.MapGet("/operations/tenants/{tenant}/projects", async (TenantId tenant, ISender sender, CancellationToken cancellationToken)
            => Results.Ok((await sender.Send(new TenantProjects(tenant), cancellationToken)).Select(project => new
            {
                project.Id,
                project.Number,
                project.Name,
                project.UnitId,
                project.State,
                project.OwnerSeat,
                plannedFrom = project.Planned?.From,
                plannedUntil = project.Planned?.Until,
                changedBy = new { kind = project.ChangedBy.Kind, seatId = project.ChangedBy.Seat },
            })));

        return group;
    }
}
