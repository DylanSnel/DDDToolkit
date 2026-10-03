using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.TestHost.Contracts;
using DDDToolkit.Supporting.Tenancy.TestHost.Converters;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Tenancy.TestHost.Persistence;

/// <summary>
/// The application's tenancy context: a plain context in a schema of its own, with Tenancy's tables closed
/// over the application's classes, its invitations, the outbox its domain events go to, and the access history
/// they are kept in. <c>AddTenancy()</c>, <c>AddTenancyInvitations()</c> and <c>AddTenancyEventLogTable()</c> are
/// the ones the generator writes into this project; the tests compare the first with the generic call it forwards
/// to.
/// </summary>
/// <param name="options">The context's options.</param>
public sealed class TestTenancyContext(DbContextOptions<TestTenancyContext> options) : DbContext(options)
{
    /// <summary>The schema Tenancy's tables are in.</summary>
    public const string Schema = "tenancy";

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        // Generated for this project's classes: HostTenant, TenantId, HostOrganization and the rest. With the
        // provider known, a tenant's root and a seat's primary placement are unique in the database as well.
        modelBuilder.AddTenancy(database: Database);

        // Invitations are the application's to have or not: the class and its id are named, the other ids are
        // filled in by the generated call.
        modelBuilder.AddTenancyInvitations<HostInvitation, InvitationId>();
        modelBuilder.AddDomainEventOutbox(Database);

        // The access history: the toolkit's event log with each event's tenant on its row. Generated for this
        // project's tenant id, like AddTenancy.
        modelBuilder.AddTenancyEventLogTable(Database);
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddTenancyConverters();
        configurationBuilder.StoreDateTimeOffsetsAsUtc();
    }
}
