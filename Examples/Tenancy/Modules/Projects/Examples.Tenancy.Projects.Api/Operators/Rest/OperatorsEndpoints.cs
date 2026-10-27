using Examples.Tenancy.Projects.Application.Operators.Commands;
using Examples.Tenancy.Projects.Application.Operators.Queries;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Projects.Api.Operators.Rest;

/// <summary>
/// What the application's own staff read and change of the projects, over HTTP: every project of one tenant, and a
/// project's name. The routes send the query and the command of the application project's <c>Operators</c> feature.
/// </summary>
/// <remarks>
/// An operator holds no seat, so a route names its tenant in the path and not in a header. The rename is an operator's
/// command. It runs as operator work in the tenant the path names, recorded as the operator.
/// <para>
/// Like every route of this project, it decides nothing and takes only the sender;
/// <see cref="ProjectsModule"/> says what all of them keep to. Internal: the module's entry maps it into the
/// group the host hands it.
/// </para>
/// </remarks>
internal static class OperatorsEndpoints
{
    /// <summary>
    /// Maps <c>GET /operations/tenants/{tenant}/projects</c> and <c>PUT /operations/tenants/{tenant}/projects/{project}/name</c>
    /// into <paramref name="group"/>, the host's group for operators.
    /// </summary>
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

        // A project's name, changed for a customer who asked: operator work in the tenant the path names.
        group.MapPut("/operations/tenants/{tenant}/projects/{project}/name", async (TenantId tenant, ProjectId project, NewName body, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new RenameTenantProject(tenant, project, body.Name ?? string.Empty), cancellationToken);
            return Results.NoContent();
        });

        return group;
    }

    /// <summary>The body of a rename: the project's new name.</summary>
    /// <param name="Name">The new name.</param>
    public sealed record NewName(string? Name);
}
