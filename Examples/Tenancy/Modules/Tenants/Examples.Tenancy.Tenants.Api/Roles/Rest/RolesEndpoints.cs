using System.Text.Json.Serialization;
using Examples.Tenancy.Tenants.Application.Roles;
using Examples.Tenancy.Tenants.Application.Roles.Commands;
using Examples.Tenancy.Tenants.Application.Roles.Queries;
using Examples.Tenancy.Tenants.Domain;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Tenants.Api.Roles.Rest;

/// <summary>
/// The tenant's roles over HTTP: listing them, creating one, setting its permissions, archiving it, and saying what it
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
    /// Maps <c>GET /tenancy/roles</c> and <c>POST /tenancy/roles</c>, <c>PUT /tenancy/roles/{id}/permissions</c> and
    /// <c>POST /tenancy/roles/{id}/archive</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapRolesEndpoints(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // Every role of the tenant, for anybody who works in it; with its permissions for whoever manages the roles.
        group.MapGet("/tenancy/roles", async (ISender sender, CancellationToken cancellationToken)
            => Results.Ok((await sender.Send(new TenantRoles(), cancellationToken)).Select(Describe)));

        group.MapPost("/tenancy/roles", async (RoleToCreate body, ISender sender, CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(new CreateTenantRole(body.Name ?? string.Empty, body.Description ?? string.Empty, body.Permissions ?? []), cancellationToken);
            return Results.Created((string?)null, new { id });
        });

        // The role's permissions become exactly the list: an empty one takes every permission off it. So the list must be there,
        // since a body that misspelt or left out the field would otherwise empty the role without a word.
        group.MapPut("/tenancy/roles/{id}/permissions", async (RoleId id, PermissionsToSet body, ISender sender, CancellationToken cancellationToken) =>
        {
            var permissions = body.Permissions ?? throw new BadHttpRequestException("The body's \"permissions\" is null: give the role's permissions as a list, an empty one to take them all off.");
            await sender.Send(new SetRolePermissions(id, permissions), cancellationToken);
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
    /// A role where a seat's overview names one: its id, its name, the starter role it was copied from and its status.
    /// The overview holds no more than the role, so its permissions, which are for whoever manages the tenant's roles,
    /// are left out, and so is whether it manages access. The list of the tenant's roles answers both.
    /// </summary>
    internal static object Describe(Role role) => new
    {
        role.Id,
        role.Name,
        role.FromStarterRole,
        role.Status,
    };

    /// <summary>
    /// A role in a list of the tenant's roles: the role's fields, as its GraphQL type shows them, with whether it manages
    /// access, which says who may give it. Its <c>permissions</c> are a list for a caller who manages the tenant's roles
    /// and <c>null</c> for anybody else, as the query says.
    /// </summary>
    internal static object Describe(RoleListing listed) => new
    {
        listed.Role.Id,
        listed.Role.Name,
        listed.Role.FromStarterRole,
        listed.Role.Status,
        permissions = listed.PermissionsShown ? listed.Role.Permissions : null,
        listed.ManagesAccess,
    };

    // The bodies.

    public sealed record RoleToCreate(string? Name, string? Description, IReadOnlyList<string>? Permissions);

    public sealed record PermissionsToSet([property: JsonRequired] IReadOnlyList<string>? Permissions);
}
