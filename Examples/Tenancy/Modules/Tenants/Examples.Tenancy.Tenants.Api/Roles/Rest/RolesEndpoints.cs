using System.Text.Json.Serialization;
using Examples.Tenancy.Tenants.Application.Roles;
using Examples.Tenancy.Tenants.Application.Roles.Commands;
using Examples.Tenancy.Tenants.Application.Roles.Queries;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Tenants.Api.Roles.Rest;

/// <summary>
/// The tenant's roles over HTTP: listing them, creating one, setting its keys, archiving it, and saying what it
/// is used for. Each route sends one command or query of the application project's <c>Roles</c> feature.
/// </summary>
/// <remarks>
/// Like every route of this project, these decide nothing and take only the sender;
/// <see cref="TenantsModule"/> says what all of them keep to. Internal: the module's entry maps them into the
/// group the host hands it.
/// </remarks>
internal static class RolesEndpoints
{
    /// <summary>
    /// Maps <c>GET /tenancy/roles</c> and <c>POST /tenancy/roles</c>, <c>PUT /tenancy/roles/{id}/keys</c> and
    /// <c>POST /tenancy/roles/{id}/archive</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapRolesEndpoints(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // Every role of the tenant, for anybody who works in it; with its keys for whoever manages the roles.
        group.MapGet("/tenancy/roles", async (ISender sender, CancellationToken cancellationToken)
            => Results.Ok((await sender.Send(new TenantRoles(), cancellationToken)).Select(Describe)));

        group.MapPost("/tenancy/roles", async (RoleToCreate body, ISender sender, CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(new CreateTenantRole(body.Name ?? string.Empty, body.Description ?? string.Empty, body.Keys ?? []), cancellationToken);
            return Results.Created((string?)null, new { id });
        });

        // The role's keys become exactly the list: an empty one takes every key off it. So the list must be there,
        // since a body that misspelt or left out the field would otherwise empty the role without a word.
        group.MapPut("/tenancy/roles/{id}/keys", async (RoleId id, KeysToSet body, ISender sender, CancellationToken cancellationToken) =>
        {
            var keys = body.Keys ?? throw new BadHttpRequestException("The body's \"keys\" is null: give the role's keys as a list, an empty one to take them all off.");
            await sender.Send(new SetRoleKeys(id, keys), cancellationToken);
            return Results.NoContent();
        });

        group.MapPost("/tenancy/roles/{id}/archive", async (RoleId id, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new ArchiveTenantRole(id), cancellationToken);
            return Results.NoContent();
        });

        return group;
    }

    /// <summary>
    /// A role where a seat's overview shows one: a role the seat holds itself, so with its keys, which a seat may
    /// always read of its own. Whether a role manages access says who may give it: only someone who holds its
    /// keys that do, or whoever manages grants where the seat is placed.
    /// </summary>
    internal static object Describe(TenancyUseCases.RoleSummary role) => new
    {
        role.Id,
        role.Name,
        role.FromPack,
        role.Status,
        role.Keys,
        role.ManagesAccess,
    };

    /// <summary>
    /// A role in a list of the tenant's roles: the same, and what the tenant uses it for, which says whether it may
    /// be given on a crew. Its <c>keys</c> are a list for a caller who manages the tenant's roles and <c>null</c>
    /// for anybody else: the query left them out.
    /// </summary>
    internal static object Describe(RoleListing listed) => new
    {
        listed.Id,
        listed.Name,
        listed.FromPack,
        listed.Status,
        listed.Keys,
        listed.ManagesAccess,
    };

    // The bodies.

    public sealed record RoleToCreate(string? Name, string? Description, IReadOnlyList<string>? Keys);

    public sealed record KeysToSet([property: JsonRequired] IReadOnlyList<string>? Keys);
}
