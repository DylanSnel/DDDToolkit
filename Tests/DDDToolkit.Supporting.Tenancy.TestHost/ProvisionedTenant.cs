using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.TestHost.Contracts;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using DDDToolkit.Supporting.Tenancy.UseCases;

namespace DDDToolkit.Supporting.Tenancy.TestHost;

/// <summary>
/// The ids a tenant the tests provision was given: the tenant, its root, its first administrator's seat and the role of
/// each starter role. <c>TenantManager.ProvisionAsync</c> returns the tenant alone, so the tests pass these ids in, as an import
/// or seeding does, and keep them (<see cref="ProvisioningWithIds"/>).
/// </summary>
/// <param name="Tenant">The tenant, and its organization.</param>
/// <param name="RootUnit">The root unit.</param>
/// <param name="AdminSeat">The first administrator's seat.</param>
/// <param name="AdministratorRole">The role copied from the administrators' starter role.</param>
/// <param name="RolesByStarterRole">Every role copied from a starter role, by starter role key.</param>
public sealed record ProvisionedTenant(
    TenantId Tenant,
    OrganizationUnitId RootUnit,
    SeatId AdminSeat,
    RoleId AdministratorRole,
    IReadOnlyDictionary<string, RoleId> RolesByStarterRole);

/// <summary>Provisioning with fixed ids, the way an import or seeding does, so a test knows the ids it was given.</summary>
public static class ProvisioningWithIds
{
    /// <summary>
    /// Provisions <paramref name="command"/> with the ids it names, and new ones for those it leaves out: the tenant,
    /// its root, the first seat and a role for every starter role seeded for its shape.
    /// </summary>
    /// <param name="tenants">The tenant manager.</param>
    /// <param name="catalogue">The catalogue the tenant's roles are copied from.</param>
    /// <param name="command">What to provision.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The ids the tenant was given.</returns>
    public static async Task<ProvisionedTenant> ProvisionWithIdsAsync(
        this TenantManager<HostTenant, TenantId, HostOrganization, HostUnit, OrganizationUnitId, HostSeat, SeatId, HostRole, RoleId> tenants,
        TenancyCatalogue catalogue,
        TenantToProvision command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenants);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(command);

        // The ids the command gives stay, a starter role the tenant is not given among them, which the manager refuses.
        var roles = new Dictionary<string, RoleId>(command.RoleIds ?? new Dictionary<string, RoleId>(), StringComparer.Ordinal);
        foreach (var starterRole in catalogue.StarterRolesFor(command.Shape))
        {
            roles.TryAdd(starterRole.Key, RoleId.Create());
        }

        var withIds = command with
        {
            TenantId = command.TenantId ?? TenantId.Create(),
            RootId = command.RootId ?? OrganizationUnitId.Create(),
            AdminSeatId = command.AdminSeatId ?? SeatId.Create(),
            RoleIds = roles,
        };

        var tenant = await tenants.ProvisionAsync(withIds, cancellationToken);
        return new ProvisionedTenant(
            tenant.Id,
            withIds.RootId!.Value,
            withIds.AdminSeatId!.Value,
            roles[catalogue.AdministratorStarterRoleFor(command.Shape).Key],
            roles);
    }
}
