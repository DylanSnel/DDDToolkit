using Examples.Tenancy.Tenants.Application.Access;
using Examples.Tenancy.Tenants.Application.Roles;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Directory.Queries;

/// <summary>
/// What the roles with these ids are called, the active ones first, each by name, with whether it manages access.
/// What a screen asks once another module answered it role ids.
/// </summary>
/// <remarks>
/// It requires a caller who works in the tenant, and the package's directory answers such a caller about any role
/// of that tenant. An id of another tenant, or of no role, is left out of the answer without a word; more ids than
/// one question takes are refused. A role's permissions are answered only to a caller who manages the tenant's roles
/// (<see cref="RoleListing.ManageRolesPermission"/>), as in the list of them.
/// </remarks>
/// <param name="Ids">The roles asked about.</param>
[InTenant]
public sealed record RolesById(IReadOnlyList<RoleId> Ids) : IQuery<IReadOnlyList<RoleListing>>, ITenantsRequest;

/// <summary>
/// Answers <see cref="RolesById"/> from the Tenancy package's directory, and whether the caller is answered the
/// permissions asked beside it.
/// </summary>
/// <param name="reads">Where Tenancy is read: the directory in a scope of this query's own, the permission on a context of its own.</param>
/// <param name="tenancy">Tenancy, asked about the current caller.</param>
public sealed class RolesByIdHandler(ITenancyReads reads, TenancyAccessManager tenancy) : IQueryHandler<RolesById, IReadOnlyList<RoleListing>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// The caller's own refusal when it is nobody, or <c>tenancy.too-many-ids</c>.
    /// </exception>
    public async ValueTask<IReadOnlyList<RoleListing>> Handle(RolesById query, CancellationToken cancellationToken)
    {
        // The directory first: it refuses a caller who may not ask, and a question with too many ids.
        var roles = await reads.AskAsync(async (RoleManager manager) => RoleListing.Judged(await manager.ByIdAsync(query.Ids, cancellationToken), manager));
        return roles.Count == 0
            ? []
            : RoleListing.From(roles, withKeys: await AtTenantPermission.IsHeldAsync(tenancy, reads, RoleListing.ManageRolesPermission, cancellationToken));
    }
}
