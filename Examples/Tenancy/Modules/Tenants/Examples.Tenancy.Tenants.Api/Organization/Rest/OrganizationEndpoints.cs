using System.Text.Json.Serialization;
using Examples.Tenancy.Tenants.Application.Organization;
using Examples.Tenancy.Tenants.Application.Organization.Commands;
using Examples.Tenancy.Tenants.Application.Organization.Queries;
using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.ValueObjects;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Tenants.Api.Organization.Rest;

/// <summary>
/// The organization's units over HTTP: listing them, adding one, moving one under another parent and archiving
/// one. Each route sends one command or query of the application project's <c>Organization</c> feature.
/// </summary>
/// <remarks>
/// Like every route of this project, these decide nothing and take only the sender;
/// <see cref="TenantsModule"/> says what all of them keep to. Internal: the module's entry maps them into the
/// group the host hands it.
/// </remarks>
internal static class OrganizationEndpoints
{
    /// <summary>
    /// Maps <c>GET /tenancy/units</c> and <c>POST /tenancy/units</c>, <c>PUT /tenancy/units/{id}/parent</c> and
    /// <c>POST /tenancy/units/{id}/archive</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // A seat sees the units it is placed in and every unit below them; system work sees them all.
        group.MapGet("/tenancy/units", async (ISender sender, CancellationToken cancellationToken)
            => Results.Ok((await sender.Send(new OrganizationUnits(), cancellationToken)).Select(Describe)));

        // A missing name is passed on empty, so the package refuses it by its code rather than by a null reference.
        // A missing kind is no kind; one the application does not have fails to bind, a 400 invalid-request. The
        // answer is the new id; there is no route of one unit, so no Location either.
        group.MapPost("/tenancy/units", async (UnitToAdd body, ISender sender, CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(new AddOrganizationUnit(body.ParentId, body.Name ?? string.Empty, body.Kind), cancellationToken);
            return Results.Created((string?)null, new { id });
        });

        group.MapPut("/tenancy/units/{id}/parent", async (OrganizationUnitId id, NewParent body, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new MoveOrganizationUnit(id, body.ParentId), cancellationToken);
            return Results.NoContent();
        });

        group.MapPost("/tenancy/units/{id}/archive", async (OrganizationUnitId id, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new ArchiveOrganizationUnit(id), cancellationToken);
            return Results.NoContent();
        });

        return group;
    }

    /// <summary>A unit where a seat's overview points at one: its id and its path from the root.</summary>
    internal static object Describe(TenancyUseCases.UnitRef unit) => new { unit.Id, unit.Path };

    /// <summary>A unit as every list of this project writes it, its kind by the name a request gives it in.</summary>
    internal static object Describe(UnitListing unit) => new
    {
        unit.Id,
        unit.ParentId,
        unit.Name,
        unit.Kind,
        unit.Status,
        unit.Path,
        unit.Depth,
    };

    // The bodies. An id a command cannot do without is required: left out, it would bind as an empty id and come
    // back as a refusal about a unit nobody named, where a 400 invalid-request says what is wrong.

    public sealed record UnitToAdd([property: JsonRequired] OrganizationUnitId ParentId, string? Name, UnitKind? Kind);

    public sealed record NewParent([property: JsonRequired] OrganizationUnitId ParentId);
}
