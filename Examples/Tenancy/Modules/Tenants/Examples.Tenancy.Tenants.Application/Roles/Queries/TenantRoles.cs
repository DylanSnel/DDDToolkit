using Mediator;

namespace Examples.Tenancy.Tenants.Application.Roles.Queries;

/// <summary>
/// The roles of the caller's tenant, the active ones first, each with whether it manages access (whether only
/// someone who holds its permissions that do may give it); and, for a caller who manages the tenant's roles, with its
/// permissions.
/// </summary>
/// <remarks>
/// It requires a caller who works in the tenant, whom the package's directory answers. Which permissions a role brings is
/// this application's to keep to those who decide it (<see cref="RoleListing.ManageRolesPermission"/>), and the handler holds
/// that itself, so the route and the GraphQL field answer alike.
/// </remarks>
[InTenant]
public sealed record TenantRoles : IQuery<IReadOnlyList<RoleListing>>, ITenantsRequest;

/// <summary>
/// Answers <see cref="TenantRoles"/> from the Tenancy package's directory, and whether the caller is answered the
/// permissions asked beside it.
/// </summary>
/// <param name="reads">Where Tenancy is read: the directory in a scope of this query's own, the permission on a context of its own.</param>
/// <param name="tenancy">Tenancy, asked about the current caller.</param>
public sealed class TenantRolesHandler(ITenancyReads reads, SampleTenancy tenancy) : IQueryHandler<TenantRoles, IReadOnlyList<RoleListing>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">The caller's own refusal when it is nobody.</exception>
    public async ValueTask<IReadOnlyList<RoleListing>> Handle(TenantRoles query, CancellationToken cancellationToken)
    {
        // The directory first: it refuses a caller who may not ask, before anything else is read.
        var roles = await reads.AskDirectoryAsync(directory => directory.ListRolesAsync(cancellationToken));
        return RoleListing.From(roles, withKeys: await AtTenantPermission.IsHeldAsync(tenancy, reads, RoleListing.ManageRolesPermission, cancellationToken));
    }
}
