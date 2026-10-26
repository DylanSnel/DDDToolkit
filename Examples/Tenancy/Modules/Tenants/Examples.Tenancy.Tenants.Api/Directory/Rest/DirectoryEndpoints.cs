using System.Text.Json.Serialization;
using Examples.Tenancy.Tenants.Api.Organization.Rest;
using Examples.Tenancy.Tenants.Api.Roles.Rest;
using Examples.Tenancy.Tenants.Api.Seats.Rest;
using Examples.Tenancy.Tenants.Application.Directory.Queries;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Tenants.Api.Directory.Rest;

/// <summary>
/// The directory by id, over HTTP: what seats, units and roles are called. Another module's answers carry ids and
/// no names, so a client that shows them asks here, with the ids it was answered. Each route sends one query of
/// the application project's <c>Directory</c> feature.
/// </summary>
/// <remarks>
/// Each is a <c>POST</c> that only asks, and changes nothing: the ids travel in the body, since as many as one
/// question takes do not fit a request line. Anyone seated in the tenant may ask, about any seat, unit or role
/// of that tenant. An id of another tenant, or of nothing, is left out of the answer, which is still a 200:
/// the answer never says which. More ids than a question takes is a 400 <c>tenancy.too-many-ids</c>.
/// <para>
/// Like every route of this project, these decide nothing and take only the sender;
/// <see cref="TenantsModule"/> says what all of them keep to. A seat, a unit and a role read here as they do
/// in the lists of the features that own them, so each is described by that feature. Internal: the module's entry
/// maps them into the group the host hands it.
/// </para>
/// </remarks>
internal static class DirectoryEndpoints
{
    /// <summary>Maps <c>POST /tenancy/directory/seats</c>, <c>/units</c> and <c>/roles</c>.</summary>
    public static IEndpointRouteBuilder MapDirectoryEndpoints(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // By name: id, name and status, never an identity.
        group.MapPost("/tenancy/directory/seats", async (IdsAsked<SeatId> body, ISender sender, CancellationToken cancellationToken)
            => Results.Ok((await sender.Send(new SeatsById(Asked(body)), cancellationToken)).Select(SeatsEndpoints.Describe)));

        // By path, whichever of them the caller is placed under.
        group.MapPost("/tenancy/directory/units", async (IdsAsked<OrganizationUnitId> body, ISender sender, CancellationToken cancellationToken)
            => Results.Ok((await sender.Send(new OrganizationUnitsById(Asked(body)), cancellationToken)).Select(OrganizationEndpoints.Describe)));

        // The active ones first, by name, with whether each manages access.
        group.MapPost("/tenancy/directory/roles", async (IdsAsked<RoleId> body, ISender sender, CancellationToken cancellationToken)
            => Results.Ok((await sender.Send(new RolesById(Asked(body)), cancellationToken)).Select(RolesEndpoints.Describe)));

        return group;
    }

    /// <summary>The ids of a body, which must be a list: an empty one asks about nothing, and is answered nothing.</summary>
    private static IReadOnlyList<TId> Asked<TId>(IdsAsked<TId> body)
        => body.Ids ?? throw new BadHttpRequestException("The body's \"ids\" is null: give the ids to ask about as a list.");

    /// <summary>The ids a question to the directory asks about: of seats, of units or of roles.</summary>
    public sealed record IdsAsked<TId>([property: JsonRequired] IReadOnlyList<TId>? Ids);
}
