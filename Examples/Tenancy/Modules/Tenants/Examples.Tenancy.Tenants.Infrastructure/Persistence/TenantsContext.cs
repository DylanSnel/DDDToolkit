using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Outbox;
using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.ValueObjects;
using Examples.Tenancy.Tenants.Infrastructure.Converters;
using Examples.Hosting;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Examples.Tenancy.Tenants.Infrastructure.Persistence;

/// <summary>
/// Tenancy's own database: the tenants, their organizations and units, the seats with their placements and
/// grants, the roles, and the rows the package writes from them on every save (the closure of each tree and
/// the rights each seat holds where).
/// </summary>
/// <remarks>
/// A plain context. Every table comes from one generated call, <c>AddTenancy()</c>, closed over this module's
/// classes and ids, which the domain and contracts projects declare; the fields and rules the classes add
/// (<see cref="OrganizationUnit.Kind"/>, <see cref="OrganizationUnit.CostCentre"/>, <see cref="Seat.JobTitle"/>)
/// are mapped by the toolkit's conventions like any aggregate's, the kind by its key
/// (<see cref="UnitKindKeyConverter"/>). The save check, and the writer of what a save changes of the access
/// questions' tables, arrive with the options instead: <c>AddTenancy</c> brings them, and <c>UseDDDToolkit</c>, in
/// the options the module passes to <see cref="PostgresPools.AddContext{TContext,TFactory}"/>, puts them on.
/// <para>
/// Invitations are the application's to have or not, so they are a call of their own,
/// <c>AddTenancyInvitations</c>: two more tables, the invitations and, apart from them, the digests of their tokens.
/// </para>
/// </remarks>
public sealed class TenantsContext(DbContextOptions<TenantsContext> options) : DbContext(options)
{
    /// <summary>The schema Tenancy's tables live in, with the module's migration history.</summary>
    public const string Schema = "tenancy";

    /// <summary>The outbox table, named after the module, so a table says whose it is without its schema.</summary>
    public const string OutboxTable = "TenancyOutboxMessages";

    /// <summary>
    /// The table of the access history: every event of Tenancy's that changes who may do what, kept for good,
    /// each with its tenant and with who acted. Named after the module for the same reason as the outbox.
    /// </summary>
    public const string HistoryTable = "TenancyEventLog";

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Seat> Seats => Set<Seat>();

    public DbSet<Role> Roles => Set<Role>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        // Generated into this project, closed over Tenant, Organization, OrganizationUnit, Seat, Role
        // and their ids, which this project does not declare: the domain project does, and it is of the same
        // module. The package's generic AddTenancy<…9 types…>() with the arguments filled in. Given the provider,
        // it also makes a tenant's root and a seat's primary placement unique in the database.
        modelBuilder.AddTenancy(database: Database);

        // The invitations, which the application chose to have: the class and its id are named, and the generated
        // call fills in the other ids. A token is never stored; its digest is, in a table of its own that reading
        // invitations does not read.
        modelBuilder.AddTenancyInvitations<Invitation, InvitationId>();

        // Tenancy raises domain events, and they are stored with the change that raised them. None leaves the
        // module: the module's registration, TenantsInfrastructure, keeps them off the sinks.
        modelBuilder.AddDomainEventOutbox(Database, tableName: OutboxTable, schema: Schema);

        // And kept, in a table that only grows: who was given what, where, by whom. The outbox row is deleted once
        // it is handled; the history's row stays. The package's policies keep a tenant's history to
        // that tenant, and a guard refuses every change to a row.
        modelBuilder.AddTenancyEventLogTable(Database, tableName: HistoryTable, schema: Schema);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // The same conventions in every context: read-only collections, [Internal] members, Version.
        configurationBuilder.AddDDDToolkitConventions();

        // Every instant as UTC, whatever offset it was written with: a grant's period is compared in SQL, here
        // and in the modules that read Tenancy's rows inside their own queries, and the driver takes no other
        // offset for a timestamp with time zone.
        configurationBuilder.StoreDateTimeOffsetsAsUtc();

        // The four ids. The contracts project declares them without Entity Framework, so this project's generated
        // registration stores them, through SingleValueConverter, as the module's own.
        configurationBuilder.AddTenantsConverters();

        // What kind of unit a unit is, the unit class's own field, by its key: a row read by hand says region, not 1,
        // a kind added later never renumbers the ones before it, and the rows written while the kind was a key of
        // the package's catalogue read the same as the rows written since. A new application that has no such rows
        // writes HaveConversion<string>() here, and stores the name.
        configurationBuilder.Properties<UnitKind>().HaveConversion<UnitKindKeyConverter>().HaveMaxLength(16);
    }
}
