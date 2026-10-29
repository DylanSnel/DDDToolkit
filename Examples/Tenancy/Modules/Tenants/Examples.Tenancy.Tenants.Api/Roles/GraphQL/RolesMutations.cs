using Examples.Tenancy.Tenants.Application.Roles.Commands;
using Examples.Tenancy.Tenants.Domain;
using HotChocolate;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Roles.GraphQL;

/// <summary>
/// What changes the tenant's roles. A mutation sends the command its route sends and answers the role the command
/// answers; a refusal arrives in the payload's <c>errors</c>.
/// </summary>
internal static class RolesMutations
{
    /// <summary>Creates a role of the tenant's own, with the permissions it brings.</summary>
    [Mutation]
    public static async Task<Role> RoleCreateAsync(string name, string? description, IReadOnlyList<string>? permissions, [Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new CreateTenantRole(name, description ?? string.Empty, permissions ?? []), cancellationToken);

    /// <summary>
    /// Makes the role's permissions exactly the list: an empty one takes every permission off it, so the list is required.
    /// </summary>
    [Mutation]
    public static async Task<Role> RolePermissionsSetAsync(RoleId id, IReadOnlyList<string> permissions, [Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new SetRolePermissions(id, permissions), cancellationToken);

    /// <summary>Archives a role: it is given to nobody from now on.</summary>
    [Mutation]
    public static async Task<Role> RoleArchiveAsync(RoleId id, [Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new ArchiveTenantRole(id), cancellationToken);
}
