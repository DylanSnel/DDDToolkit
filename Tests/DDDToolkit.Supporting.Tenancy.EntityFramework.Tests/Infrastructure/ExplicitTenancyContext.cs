using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.Supporting.Tenancy.TestHost.Converters;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests.Infrastructure;

/// <summary>
/// <see cref="TestTenancyContext"/>'s model, with the package's generic <c>AddTenancy</c> and
/// <c>AddTenancyInvitations</c> closed over the TestHost's classes by hand. The TestHost's context calls the ones
/// the generator wrote for it, and the two models are compared: a wrapper is only right when it builds exactly
/// this one.
/// </summary>
public sealed class ExplicitTenancyContext(DbContextOptions<ExplicitTenancyContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(TestTenancyContext.Schema);
        modelBuilder.AddTenancy<HostTenant, TenantId, HostOrganization, HostUnit, OrganizationUnitId, HostSeat, SeatId, HostRole, RoleId>(database: Database);
        modelBuilder.AddTenancyInvitations<HostInvitation, InvitationId, TenantId, OrganizationUnitId, RoleId, SeatId>();
        modelBuilder.AddDomainEventOutbox(Database);
        modelBuilder.AddTenancyEventLogTable<TenantId>(Database);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddTenancyConverters();
        configurationBuilder.StoreDateTimeOffsetsAsUtc();
    }
}
