using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace Examples.Tenancy.Tenants.Application.Roles;

/// <summary>
/// A role as this application lists it: what the Tenancy package says of it, with its keys only for whoever
/// decides them.
/// </summary>
/// <remarks>
/// Anybody who works in the tenant reads what a role is called. What it lets its holder do, its keys, is for
/// those who decide that: a caller who holds <see cref="KeysKey"/> for the whole tenant.
/// The queries that list roles hold that themselves, so every way a role is answered keeps to it: a route, a
/// GraphQL field, the answer of a mutation. The record is the application's own for that reason: it has no way
/// to carry the keys of a role to a caller they are not for. The roles a seat holds itself are another answer,
/// its own overview, and those it may always read.
/// </remarks>
/// <param name="Id">The role.</param>
/// <param name="Name">Its name.</param>
/// <param name="FromPack">The pack it was copied from, or <see langword="null"/> for a role the tenant made.</param>
/// <param name="Status">Whether it can still be given.</param>
/// <param name="Keys">
/// The permission keys it brings, or <see langword="null"/> for a caller who does not hold <see cref="KeysKey"/>:
/// not an empty list, which would say the role brings none.
/// </param>
/// <param name="ManagesAccess">
/// Whether one of its keys manages access, and so, while containment is on, as it is in the sample, it is given only
/// by a seat that holds those keys.
/// </param>
public sealed record RoleListing(
    RoleId Id,
    string Name,
    string? FromPack,
    RoleStatus Status,
    IReadOnlyList<string>? Keys,
    bool ManagesAccess)
{
    /// <summary>The key a caller holds for the whole tenant to be answered <see cref="Keys"/>: the one that manages the tenant's roles.</summary>
    public const string KeysKey = TenancyKeys.RolesManage;

    /// <summary>
    /// <paramref name="roles"/>, in the order the directory answered them, each with its keys only when
    /// <paramref name="withKeys"/>.
    /// </summary>
    /// <param name="roles">The roles, as the package's directory answered them.</param>
    /// <param name="withKeys">Whether the caller holds <see cref="KeysKey"/> for the whole tenant.</param>
    internal static IReadOnlyList<RoleListing> From(IReadOnlyList<TenancyUseCases.RoleSummary> roles, bool withKeys)
        => [.. roles.Select(role => new RoleListing(
            role.Id,
            role.Name,
            role.FromPack,
            role.Status,
            withKeys ? role.Keys : null,
            role.ManagesAccess))];
}
