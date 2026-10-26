using Examples.Tenancy.Tenants.Api.Directory.GraphQL;
using Examples.Tenancy.Tenants.Application.Roles;
using Examples.Tenancy.Tenants.Application.Roles.Commands;
using HotChocolate;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Roles.GraphQL;

/// <summary>
/// What changes the tenant's roles. A mutation sends the command its route sends, then reads the role it changed
/// and answers that; a refusal arrives in the payload's <c>errors</c>.
/// </summary>
internal static class RolesMutations
{
    /// <summary>Creates a role of the tenant's own, with the keys it brings.</summary>
    [Mutation]
    public static async Task<RoleListing?> RoleCreateAsync(string name, string? description, IReadOnlyList<string>? keys, [Service] ISender sender, CancellationToken cancellationToken)
    {
        var id = await sender.Send(new CreateTenantRole(name, description ?? string.Empty, keys ?? []), cancellationToken);
        return await sender.RoleNowAsync(id, cancellationToken);
    }

    /// <summary>
    /// Makes the role's keys exactly the list: an empty one takes every key off it, so the list is required.
    /// </summary>
    [Mutation]
    public static async Task<RoleListing?> RoleKeysSetAsync(RoleId id, IReadOnlyList<string> keys, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new SetRoleKeys(id, keys), cancellationToken);
        return await sender.RoleNowAsync(id, cancellationToken);
    }

    /// <summary>Archives a role: it is given to nobody from now on.</summary>
    [Mutation]
    public static async Task<RoleListing?> RoleArchiveAsync(RoleId id, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new ArchiveTenantRole(id), cancellationToken);
        return await sender.RoleNowAsync(id, cancellationToken);
    }
}
