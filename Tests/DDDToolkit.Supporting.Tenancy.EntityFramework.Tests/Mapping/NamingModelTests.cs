using System.Text.RegularExpressions;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.Supporting.Tenancy.TestHost.Converters;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// What <c>AddTenancy</c> leaves to an application's own naming, and what it names itself. The tables and the two
/// filtered indexes are named with <see cref="TenancyTableNames"/>, and stay as named under a naming convention;
/// every column, key and other index is the context's to name; the condition of a filtered index reads its column
/// by the name the model has for it; and a row read from one of Tenancy's functions is no table, whatever a
/// convention calls it. No test here connects to a database: each builds a model, and the script a migration
/// would run.
/// </summary>
public sealed partial class NamingModelTests
{
    private const string RootIndex = "IX_OrganizationUnits_TenantId_WhereRoot";
    private const string PrimaryIndex = "IX_SeatPlacements_SeatId_WherePrimary";

    /// <summary>Each provider, with the conditions of the two indexes over columns named <c>above</c> and <c>is_first</c>.</summary>
    public static TheoryData<string, string, string> Providers => new()
    {
        { "Sqlite", "\"above\" IS NULL", "\"is_first\" = 1" },
        { "Npgsql", "\"above\" IS NULL", "\"is_first\"" },
        { "SqlServer", "[above] IS NULL", "[is_first] = CAST(1 AS bit)" },
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public void The_conditions_of_the_filtered_indexes_read_the_columns_by_the_names_the_model_has(string provider, string root, string primary)
    {
        // An application that names the two columns itself, before it maps Tenancy.
        using var context = new ColumnsNamedFirst(On<ColumnsNamedFirst>(provider));

        Index<HostUnit>(context, RootIndex).GetFilter().Should().Be(root);
        Index<Placement<SeatId, OrganizationUnitId, RoleId>>(context, PrimaryIndex).GetFilter().Should().Be(primary);

        // And the script a migration runs makes each index over the column the table has.
        var script = context.Database.GenerateCreateScript();
        script.Should().Contain("WHERE " + root).And.Contain("WHERE " + primary);
        script.Should().NotContain("ParentId").And.NotContain("IsPrimary");
    }

    [Fact]
    public void The_filtered_indexes_are_named_after_their_tables_unless_the_table_names_name_them()
    {
        using var renamed = new TablesRenamed(On<TablesRenamed>("Npgsql"));
        using var named = new IndexesNamed(On<IndexesNamed>("Npgsql"));

        Filtered(renamed).Should().BeEquivalentTo(["IX_Units_TenantId_WhereRoot", "IX_Placements_SeatId_WherePrimary"]);
        Filtered(named).Should().BeEquivalentTo(["units_root_of_a_tenant", "placements_primary_of_a_seat"]);

        static IEnumerable<string?> Filtered(DbContext context)
            => context.Model.GetEntityTypes().SelectMany(entityType => entityType.GetIndexes()).Where(index => index.GetFilter() is not null).Select(index => index.GetDatabaseName());
    }

    [Fact]
    public void The_snake_case_names_are_the_default_names_in_snake_case()
    {
        var (plain, snake) = (TenancyTableNames.Default, TenancyTableNames.SnakeCase);

        (snake.Tenants, snake.Organizations, snake.Units, snake.UnitPaths, snake.Seats, snake.Placements, snake.Grants, snake.Rights, snake.Roles, snake.AccessRevisions)
            .Should().Be((
                InSnakeCase(plain.Tenants), InSnakeCase(plain.Organizations), InSnakeCase(plain.Units), InSnakeCase(plain.UnitPaths), InSnakeCase(plain.Seats),
                InSnakeCase(plain.Placements), InSnakeCase(plain.Grants), InSnakeCase(plain.Rights), InSnakeCase(plain.Roles), InSnakeCase(plain.AccessRevisions)));
        (snake.Seats, snake.Units, snake.AccessRevisions).Should().Be(("seats", "organization_units", "tenancy_access_revisions"));

        // The two indexes Tenancy names itself, as a snake_case convention would have named them from the default names.
        (snake.RootIndex, snake.PrimaryIndex).Should().Be((InSnakeCase(RootIndex), InSnakeCase(PrimaryIndex)));
        (plain.RootIndex, plain.PrimaryIndex).Should().Be((null, null), "the default names are made from the tables' names");
    }

    [Fact]
    public void Under_a_naming_convention_tenancy_keeps_the_names_it_gave_and_follows_the_ones_it_was_given()
    {
        using var tenancy = new ConventionNamed(On<ConventionNamed>("Npgsql"));
        using var module = new ConventionNamedModule(On<ConventionNamedModule>("Npgsql"));

        // The tables are the ones AddTenancy named, and the application's own entity on a tenant, which the convention
        // named. The rows the store reads from Tenancy's functions are none, though the convention gave each a table
        // name as it was added; nor are the rows read through views over those tables.
        tenancy.Model.GetRelationalModel().Tables.Select(table => table.Name).Should().BeEquivalentTo(
        [
            "Tenants", "Organizations", "OrganizationUnits", "OrganizationUnitPaths", "Seats", "SeatPlacements", "SeatRoleGrants", "SeatRights", "Roles",
            "TenancyAccessRevisions", "t_tenantnote",
        ]);
        module.Model.GetRelationalModel().Tables.Should().BeEmpty("a module that reads Tenancy through its functions maps no table for them");
        module.Database.GenerateCreateScript().Should().NotContain("CREATE TABLE");

        // The columns are the convention's, and the conditions of the two indexes read them by those names.
        var units = tenancy.Model.FindEntityType(typeof(HostUnit))!;
        units.FindProperty(nameof(HostUnit.ParentId))!.GetColumnName().Should().Be("c_parentid");
        Index<HostUnit>(tenancy, RootIndex).GetFilter().Should().Be("\"c_parentid\" IS NULL");
        Index<Placement<SeatId, OrganizationUnitId, RoleId>>(tenancy, PrimaryIndex).GetFilter().Should().Be("\"c_isprimary\"");

        // Those two keep their names. The convention names an index after its table and columns, which for the
        // root's index would be the name of the plain index on the same column.
        units.GetIndexes().Select(index => index.GetDatabaseName()).Should().BeEquivalentTo([RootIndex, "i_ix_organizationunits_c_tenantid", "i_ix_organizationunits_c_parentid"]);

        // What a function answers under is its own, in Tenancy's context and in a module's.
        tenancy.Model.FindEntityType(typeof(MoveReach<OrganizationUnitId>))!.GetProperties().Select(property => property.GetColumnName())
            .Should().BeEquivalentTo(["UnitId", "Key", "EndsAt", "Parent", "OfCaller"]);
        module.Model.GetEntityTypes().Single(entityType => entityType.GetFunctionName() is null).GetProperties().Select(property => property.GetColumnName())
            .Should().Equal(["SeatId"], "who holds a key at a unit is the one row a module reads from SQL of the store's");
    }

    private static IIndex Index<TEntity>(DbContext context, string name)
        => context.Model.FindEntityType(typeof(TEntity))!.GetIndexes().Single(index => index.GetDatabaseName() == name);

    /// <summary><paramref name="name"/> as a snake_case convention writes it: <c>IX_SeatRights</c> as <c>ix_seat_rights</c>.</summary>
    private static string InSnakeCase(string name) => WordStarts().Replace(name, "_").ToLowerInvariant();

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex WordStarts();

    /// <summary>Options for a context that is asked for its model only: it never connects.</summary>
    private static DbContextOptions<TContext> On<TContext>(string provider)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>();
        return (provider switch
        {
            "Npgsql" => options.UseNpgsql("Host=model-only"),
            "Sqlite" => options.UseSqlite("Data Source=:memory:"),
            "SqlServer" => options.UseSqlServer("Server=model-only"),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "No such provider here."),
        }).Options;
    }

    /// <summary>Tenancy's context of an application that names two of Tenancy's columns itself, before it maps Tenancy.</summary>
    private sealed class ColumnsNamedFirst(DbContextOptions<ColumnsNamedFirst> options) : HostContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<HostOrganization>().OwnsMany(organization => organization.Units).Property(unit => unit.ParentId).HasColumnName("above");
            modelBuilder.Entity<HostSeat>().OwnsMany(seat => seat.Placements).Property(placement => placement.IsPrimary).HasColumnName("is_first");
            Map(modelBuilder);
        }
    }

    /// <summary>Tenancy's context of an application that renames two tables.</summary>
    private sealed class TablesRenamed(DbContextOptions<TablesRenamed> options) : HostContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => Map(modelBuilder, new TenancyTableNames(Units: "Units", Placements: "Placements"));
    }

    /// <summary>Tenancy's context of an application that names the two filtered indexes.</summary>
    private sealed class IndexesNamed(DbContextOptions<IndexesNamed> options) : HostContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => Map(modelBuilder, TenancyTableNames.Default with { RootIndex = "units_root_of_a_tenant", PrimaryIndex = "placements_primary_of_a_seat" });
    }

    /// <summary>Tenancy's context of an application with a naming convention.</summary>
    private sealed class ConventionNamed(DbContextOptions<ConventionNamed> options) : HostContext(options, named: true)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => Map(modelBuilder);
    }

    /// <summary>A module's context of that application, which reads Tenancy through its functions.</summary>
    private sealed class ConventionNamedModule(DbContextOptions<ConventionNamedModule> options) : HostContext(options, named: true)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.AddTenancyReadFunctions<TenantId, SeatId, OrganizationUnitId, RoleId>(TestTenancyContext.Schema);
    }

    /// <summary>A context over the TestHost's classes, with the conventions an application registers and, with <paramref name="named"/>, one that names.</summary>
    private abstract class HostContext(DbContextOptions options, bool named = false) : DbContext(options)
    {
        protected void Map(ModelBuilder modelBuilder, TenancyTableNames? tables = null)
            => modelBuilder.AddTenancy<HostTenant, TenantId, HostOrganization, HostUnit, OrganizationUnitId, HostSeat, SeatId, HostRole, RoleId>(tables, Database);

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddDDDToolkitConventions();
            configurationBuilder.AddTenancyConverters();
            configurationBuilder.StoreDateTimeOffsetsAsUtc();
            if (named)
            {
                configurationBuilder.Conventions.Add(_ => new NamesWhatIsAdded());
            }
        }
    }

    /// <summary>
    /// A naming convention as the ones that rename a whole model work: it names a table as its entity type is added
    /// to the model, a column as its property is, and an index as it is, and it takes its table name back from an
    /// entity type that is then mapped to a view or a function by name. A name the mapping gives explicitly it
    /// cannot replace.
    /// </summary>
    private sealed class NamesWhatIsAdded : IEntityTypeAddedConvention, IEntityTypeAnnotationChangedConvention, IPropertyAddedConvention, IIndexAddedConvention
    {
        public void ProcessEntityTypeAdded(IConventionEntityTypeBuilder entityTypeBuilder, IConventionContext<IConventionEntityTypeBuilder> context)
        {
            if (entityTypeBuilder.Metadata.GetTableName() is { } table)
            {
                entityTypeBuilder.ToTable("t_" + table.ToLowerInvariant());
            }
        }

        public void ProcessEntityTypeAnnotationChanged(
            IConventionEntityTypeBuilder entityTypeBuilder,
            string name,
            IConventionAnnotation? annotation,
            IConventionAnnotation? oldAnnotation,
            IConventionContext<IConventionAnnotation> context)
        {
            if (name is RelationalAnnotationNames.ViewName or RelationalAnnotationNames.FunctionName
                && annotation?.Value is not null
                && entityTypeBuilder.Metadata.GetTableNameConfigurationSource() == ConfigurationSource.Convention)
            {
                entityTypeBuilder.ToTable(null);
            }
        }

        public void ProcessPropertyAdded(IConventionPropertyBuilder propertyBuilder, IConventionContext<IConventionPropertyBuilder> context)
            => propertyBuilder.HasColumnName("c_" + propertyBuilder.Metadata.Name.ToLowerInvariant());

        public void ProcessIndexAdded(IConventionIndexBuilder indexBuilder, IConventionContext<IConventionIndexBuilder> context)
            => indexBuilder.HasDatabaseName("i_" + indexBuilder.Metadata.GetDefaultDatabaseName()?.ToLowerInvariant());
    }
}
