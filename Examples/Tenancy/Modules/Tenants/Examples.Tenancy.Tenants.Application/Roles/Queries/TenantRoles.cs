using Mediator;

namespace Examples.Tenancy.Tenants.Application.Roles.Queries;

/// <summary>
/// The roles of the caller's tenant, the active ones first: each role as the role manager read it, with whether it
/// manages access (whether only someone who holds its permissions that do may give it), and whether the caller reads
/// its permissions.
/// </summary>
/// <remarks>
/// It requires a caller who works in the tenant, whom the role manager answers. Which permissions a role brings is this
/// application's to keep to those who decide it (<see cref="RoleListing.ManageRolesPermission"/>). The handler asks
/// whether the caller holds that permission, so the route writes the permissions only for such a caller.
/// </remarks>
[InTenant]
public sealed record TenantRoles : IQuery<IReadOnlyList<RoleListing>>, ITenantsRequest;

/// <summary>
/// Answers <see cref="TenantRoles"/> from the Tenancy package's role manager, and whether the caller reads the
/// permissions, asked beside it.
/// </summary>
/// <param name="reads">Where Tenancy is read: the role manager in a scope of this query's own, the permission on a context of its own.</param>
/// <param name="tenancy">Tenancy, asked about the current caller.</param>
public sealed class TenantRolesHandler(ITenancyReads reads, TenancyAccessManager tenancy) : IQueryHandler<TenantRoles, IReadOnlyList<RoleListing>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">The caller's own refusal when it is nobody.</exception>
    public async ValueTask<IReadOnlyList<RoleListing>> Handle(TenantRoles query, CancellationToken cancellationToken)
    {
        // The role manager first: it refuses a caller who may not ask, before anything else is read.
        var roles = await reads.AskAsync(async (RoleManager manager) => RoleListing.Judged(await manager.ListAsync(cancellationToken), manager));
        return RoleListing.Listed(roles, permissionsShown: await AtTenantPermission.IsHeldAsync(tenancy, reads, RoleListing.ManageRolesPermission, cancellationToken));
    }
}
