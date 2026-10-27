using Examples.Tenancy.Tenants.Application.Access;
using Examples.Tenancy.Tenants.Application.Roles;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Directory.Queries;

/// <summary>
/// The roles with these ids, the active ones first, each as the role manager read it, with whether it manages access
/// and whether the caller reads its permissions. What a screen asks once another module answered it role ids.
/// </summary>
/// <remarks>
/// It requires a caller who works in the tenant, and the role manager answers such a caller about any role of that
/// tenant. An id of another tenant, or of no role, is left out of the answer without a word; more ids than one
/// question takes are refused. A role's permissions are shown only to a caller who manages the tenant's roles
/// (<see cref="RoleListing.ManageRolesPermission"/>), as in the list of them.
/// </remarks>
/// <param name="Ids">The roles asked about.</param>
[InTenant]
public sealed record RolesById(IReadOnlyList<RoleId> Ids) : IQuery<IReadOnlyList<RoleListing>>, ITenantsRequest;

/// <summary>
/// Answers <see cref="RolesById"/> from the Tenancy package's role manager, and whether the caller reads the
/// permissions, asked beside it.
/// </summary>
/// <param name="reads">Where Tenancy is read: the role manager in a scope of this query's own, the permission on a context of its own.</param>
/// <param name="tenancy">Tenancy, asked about the current caller.</param>
public sealed class RolesByIdHandler(ITenancyReads reads, TenancyAccessManager tenancy) : IQueryHandler<RolesById, IReadOnlyList<RoleListing>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// The caller's own refusal when it is nobody, or <c>tenancy.too-many-ids</c>.
    /// </exception>
    public async ValueTask<IReadOnlyList<RoleListing>> Handle(RolesById query, CancellationToken cancellationToken)
    {
        // The role manager first: it refuses a caller who may not ask, and a question with too many ids.
        var roles = await reads.AskAsync(async (RoleManager manager) => RoleListing.Judged(await manager.ByIdAsync(query.Ids, cancellationToken), manager));
        return roles.Count == 0
            ? []
            : RoleListing.Listed(roles, permissionsShown: await AtTenantPermission.IsHeldAsync(tenancy, reads, RoleListing.ManageRolesPermission, cancellationToken));
    }
}
