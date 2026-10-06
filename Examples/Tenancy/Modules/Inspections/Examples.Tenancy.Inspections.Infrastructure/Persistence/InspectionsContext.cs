using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Outbox;
using Examples.Tenancy.Inspections.Infrastructure.Converters;
using Examples.Hosting;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Examples.Tenancy.Inspections.Infrastructure.Persistence;

/// <summary>Inspections' own database: the inspections, and the outbox of what recording them raised.</summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>No read model.</b> Unlike Projects, this context maps nothing of Tenancy's: whether the caller may
/// see or record on a project is Projects' answer, asked through its gate, not a question this module asks in
/// its own queries.</item>
/// <item><b>The tenant.</b> <c>ScopeToTenant</c> puts the tenant filter on <see cref="Inspection"/> and marks it
/// for Tenancy's save check, which <c>UseDDDToolkit</c> adds: an inspection of another tenant is neither read nor
/// written, whoever loaded or made it.</item>
/// <item><b>Instants.</b> Every <see cref="DateTimeOffset"/> is stored as its UTC instant, whatever offset it
/// was written with, so a project's inspections are ordered in SQL.</item>
/// </list>
/// </remarks>
public sealed class InspectionsContext(DbContextOptions<InspectionsContext> options) : DbContext(options)
{
    /// <summary>The schema Inspections' tables live in, with the module's migration history.</summary>
    public const string Schema = "inspections";

    /// <summary>The inspections' table.</summary>
    public const string InspectionsTable = "Inspections";

    /// <summary>The column that holds the first of the days an inspection covers.</summary>
    public const string DaysFromColumn = "DaysFrom";

    /// <summary>The column that holds the last of the days an inspection covers.</summary>
    public const string DaysUntilColumn = "DaysUntil";

    /// <summary>The outbox table, named after the module, so a table says whose it is without its schema.</summary>
    public const string OutboxTable = "InspectionsOutboxMessages";

    public DbSet<Inspection> Inspections => Set<Inspection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Inspection>(inspection =>
        {
            inspection.ToTable(InspectionsTable);
            inspection.ScopeToTenant(row => row.TenantId);

            // Who recorded an inspection and who changed it last, as ids: see the same call in Projects' context.
            inspection.RecordsWhoChanged<Inspection, SeatId>();

            // An inspection stays with the project it was recorded on: a save that would move it is refused, and
            // the privileges the export writes leave the column out of what may be updated.
            inspection.Property(row => row.ProjectId).IsFixedAfterInsert();

            // "This project's inspections" is the question every list asks.
            inspection.HasIndex(row => row.ProjectId);

            inspection.Property(row => row.Title).HasMaxLength(Inspection.LongestTitle);

            // The days are a value object the modules share, declared without Entity Framework, so how it is
            // stored is said here: inline, as two columns of the inspection's row.
            inspection.ComplexProperty(row => row.Days, days =>
            {
                days.Property(range => range.From).HasColumnName(DaysFromColumn);
                days.Property(range => range.Until).HasColumnName(DaysUntilColumn);
            });
        });

        // Recording an inspection raises a domain event, stored with the inspection.
        modelBuilder.AddDomainEventOutbox(Database, tableName: OutboxTable, schema: Schema);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // The same conventions in every context: read-only collections, [Internal] members, Version.
        configurationBuilder.AddDDDToolkitConventions();

        // Every instant as UTC, whatever offset it was written with: inspections are ordered in SQL, and the
        // driver takes no other offset for a timestamp with time zone.
        configurationBuilder.StoreDateTimeOffsetsAsUtc();

        // The ids this context stores: the inspection's, declared in the domain project, with Tenancy's and the
        // project's. None of those projects has Entity Framework, so this project's generated registration stores
        // them all: its own module's id, and the ids the other two publish.
        configurationBuilder.AddInspectionsConverters();
    }
}
