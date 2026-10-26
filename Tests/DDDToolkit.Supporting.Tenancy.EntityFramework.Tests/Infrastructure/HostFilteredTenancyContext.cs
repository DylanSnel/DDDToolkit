using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.Supporting.Tenancy.TestHost.Converters;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests.Infrastructure;

/// <summary>
/// <see cref="TestTenancyContext"/>'s tables, with filters of the application's own on seats and roles next to
/// the tenant filter: a seat whose job title is <see cref="Hidden"/> and a role whose description is are left out
/// of every read the application makes. They still hold rights, and still give them.
/// </summary>
public sealed class HostFilteredTenancyContext(DbContextOptions<HostFilteredTenancyContext> options) : DbContext(options)
{
    /// <summary>What hides a seat or a role from the application's reads.</summary>
    public const string Hidden = "hidden";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(TestTenancyContext.Schema);
        modelBuilder.AddTenancy<HostTenant, TenantId, HostOrganization, HostUnit, OrganizationUnitId, HostSeat, SeatId, HostRole, RoleId>();
        modelBuilder.Entity<HostSeat>().HasQueryFilter("hidden-seats", seat => seat.JobTitle != Hidden);
        modelBuilder.Entity<HostRole>().HasQueryFilter("hidden-roles", role => role.Description != Hidden);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddTenancyConverters();
        configurationBuilder.StoreDateTimeOffsetsAsUtc();
    }
}
