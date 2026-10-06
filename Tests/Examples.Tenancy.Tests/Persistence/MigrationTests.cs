using System.Text.RegularExpressions;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Examples.Tenancy.Tests.Persistence;

/// <summary>
/// The modules' migrations against their models. A module has one set of them, for Postgres, in its
/// infrastructure project beside its context. Every context here is built by the module's design-time factory,
/// the way <c>dotnet ef</c> and the export build it: not by the web host, so a migration that only works because
/// of something the host adds would fail here. None of this opens a database: what a database makes of the
/// migrations is the Supabase tests' to say.
/// </summary>
public sealed partial class MigrationTests
{
    /// <summary>Each module's context, from the design-time factory the build wrote beside it.</summary>
    private static readonly IReadOnlyDictionary<string, Func<DbContext>> DesignTimeContexts = new Dictionary<string, Func<DbContext>>(StringComparer.Ordinal)
    {
        ["Tenants"] = () => new TenantsContextDesignTimeFactory().CreateDbContext([]),
        ["Projects"] = () => new ProjectsContextDesignTimeFactory().CreateDbContext([]),
        ["Inspections"] = () => new InspectionsContextDesignTimeFactory().CreateDbContext([]),
    };

    /// <summary>Each module's design-time factory: the one the build wrote beside its context, which is marked.</summary>
    private static readonly IReadOnlyDictionary<string, Type> DesignTimeFactories = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        ["Tenants"] = typeof(TenantsContextDesignTimeFactory),
        ["Projects"] = typeof(ProjectsContextDesignTimeFactory),
        ["Inspections"] = typeof(InspectionsContextDesignTimeFactory),
    };

    /// <summary>Tenancy's tables, as the package names them, its outbox, its access history and its invitations.</summary>
    private static readonly string[] TenancyTables =
    [
        "Tenants", "Organizations", "OrganizationUnits", "OrganizationUnitPaths", "Seats", "SeatPlacements",
        "SeatRoleGrants", "SeatRights", "Roles", "TenancyAccessRevisions", TenantsContext.OutboxTable,
        TenantsContext.HistoryTable, "Invitations", "InvitationDigests",
    ];

    /// <summary>Projects' tables: the projects, their crews, the roles held on them, the tenants' project roles, and its outbox.</summary>
    private static readonly string[] ProjectsTables =
    [
        ProjectsContext.ProjectsTable, ProjectsContext.CrewTable, ProjectsContext.CrewRolesTable, ProjectsContext.ProjectRolesTable, ProjectsContext.OutboxTable,
    ];

    /// <summary>Inspections' tables: the inspections, and its outbox.</summary>
    private static readonly string[] InspectionsTables =
    [
        InspectionsContext.InspectionsTable, InspectionsContext.OutboxTable,
    ];

    public static TheoryData<string> Modules => new(DesignTimeContexts.Keys);

    [Theory]
    [MemberData(nameof(Modules))]
    public void Every_module_has_no_pending_model_changes(string module)
    {
        using var context = DesignTimeContexts[module]();

        context.Database.GetMigrations().Should().NotBeEmpty("{0}'s migrations are found through its design-time factory", module);
        context.Database.HasPendingModelChanges().Should().BeFalse(
            "a change to {0}'s model needs a migration: dotnet ef migrations add, with the module's infrastructure project as its own startup project (see its design-time factory)",
            module);
    }

    /// <summary>
    /// A module has one set of migrations, and it is where its context is: in the infrastructure project, in the
    /// folder <c>Persistence/Migrations</c>, found through the one design-time factory beside the context, which the
    /// build writes because the context is marked; the marker is also how the export and the host find it. No project
    /// beside it holds a second set for another database, and nothing tells Entity Framework to look in another
    /// assembly: the running host and <c>dotnet ef</c> read the same.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modules))]
    public void Every_modules_migrations_are_beside_its_context_in_its_infrastructure_project(string module)
    {
        using var context = DesignTimeContexts[module]();
        var infrastructure = SampleLayout.InfrastructureOf(module).Assembly;
        var migrations = context.GetService<IMigrationsAssembly>();
        var factory = DesignTimeFactories[module];

        context.GetType().Assembly.Should().BeSameAs(infrastructure, "{0}'s context is its infrastructure project's", module);
        migrations.Assembly.Should().BeSameAs(infrastructure, "{0}'s migrations are in the assembly of its context", module);
        migrations.Migrations.Values.Append(migrations.ModelSnapshot!.GetType()).Select(type => type.Namespace)
            .Should().OnlyContain(name => name == infrastructure.GetName().Name + ".Persistence.Migrations", "a migration's namespace is its folder");

        factory.Assembly.Should().BeSameAs(infrastructure);
        context.GetType().IsDefined(typeof(SupabaseMigrationsAttribute), inherit: false).Should().BeTrue("the marker on the context is how the project that exports and the host find {0}'s", module);
        factory.Name.Should().Be(context.GetType().Name + "DesignTimeFactory", "{0} writes no factory of its own: the one dotnet ef finds is the build's", module);
        TypeScan.TypesOf(infrastructure).Where(type => type.GetInterfaces().Any(implemented => implemented.IsGenericType && implemented.GetGenericTypeDefinition() == typeof(Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<>)))
            .Should().Equal([factory], "one factory for one database: {0} has no second set of migrations to build a context for", module);
    }

    /// <summary>
    /// What <c>dotnet ef migrations add</c> does, by the code it runs for it: Entity Framework's design-time services
    /// find the context and the factory for it, and make the context through that factory. With the infrastructure
    /// project as its own startup project, as the context's remarks write the command, the factory is found in the
    /// startup project; with another startup project, the host's say, in the context's own assembly. Either way it is
    /// the one the build wrote, with nothing written by hand, and the context it makes keeps the migration history in
    /// the module's schema.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modules))]
    public void Dotnet_ef_makes_every_modules_context_through_the_factory_the_build_wrote(string module)
    {
        var infrastructure = SampleLayout.InfrastructureOf(module).Assembly;

        foreach (var startup in new[] { infrastructure, typeof(Program).Assembly })
        {
            var reported = new DesignTimeReport();
#pragma warning disable EF1001 // The operations dotnet ef runs, which Entity Framework keeps for its own tools.
            var operations = new Microsoft.EntityFrameworkCore.Design.Internal.DbContextOperations(
                reported, infrastructure, startup, project: infrastructure.GetName().Name + ".csproj", projectDir: AppContext.BaseDirectory,
                rootNamespace: infrastructure.GetName().Name, language: "C#", nullable: true, args: []);
            using var context = operations.CreateContext(module + "Context");
#pragma warning restore EF1001

            context.GetType().Should().BeSameAs(DesignTimeFactories[module].GetInterfaces().Single().GetGenericArguments()[0]);
            reported.Verbose.Should().Contain(
                $"Using DbContext factory '{DesignTimeFactories[module].Name}'.",
                "dotnet ef says which factory it makes {0}'s context with, with {1} as its startup project", module, startup.GetName().Name);
            context.Database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL");
            context.GetService<IHistoryRepository>().GetInsertScript(new HistoryRow("20260101000000_Probe", "10.0.0"))
                .Should().Contain($"INSERT INTO {context.Model.GetDefaultSchema()}.\"{HistoryRepository.DefaultTableName}\"", "the history is in {0}'s schema, where the host reads it", module);
        }
    }

    /// <summary>What dotnet ef's design-time services report, its verbose lines kept: they name the factory a context is made with.</summary>
#pragma warning disable EF1001
    private sealed class DesignTimeReport : Microsoft.EntityFrameworkCore.Design.Internal.IOperationReporter
#pragma warning restore EF1001
    {
        public List<string> Verbose { get; } = [];

        public void WriteError(string message)
        {
        }

        public void WriteWarning(string message)
        {
        }

        public void WriteInformation(string message)
        {
        }

        public void WriteVerbose(string message) => Verbose.Add(message);
    }

    /// <summary>
    /// A snapshot and a migration's designer file name each entity by its class's full name, as text. The model
    /// differ compares tables, so a class moved to another namespace leaves no pending change and no migration to
    /// add: the text has to follow the class by hand. Held here: the snapshot names exactly the entity types the
    /// model has, and no migration's target model names a class of the sample that is gone.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modules))]
    public void The_snapshot_and_the_migrations_name_every_class_by_the_namespace_it_has(string module)
    {
        using var context = DesignTimeContexts[module]();
        var assembly = context.GetService<IMigrationsAssembly>();
        var model = context.GetService<IDesignTimeModel>().Model;

        // A snapshot has no classes, so it names an owned type by its owner and navigation, then '#', then the
        // class; the model names it by the class alone. The class is what is compared.
        static string ClassOf(IReadOnlyEntityType entity) => entity.Name[(entity.Name.LastIndexOf('#') + 1)..];
        var inSnapshot = assembly.ModelSnapshot!.Model.GetEntityTypes().Select(ClassOf).Distinct().ToList();
        var inModel = model.GetEntityTypes().Select(ClassOf).Distinct().ToList();
        inSnapshot.Except(inModel).Should().BeEmpty("{0}'s snapshot names no entity type its model does not have", module);
        inModel.Except(inSnapshot).Should().BeEmpty("{0}'s snapshot names every entity type its model has", module);

        // Every class of the sample a name mentions, an id inside the name of one of the package's rows included.
        // Before each '#' of an owned type's name stands a navigation, which is no part of a class's name.
        static IEnumerable<string> ClassesIn(string name)
        {
            var parts = name.Split('#');
            return parts.Select((part, index) => index < parts.Length - 1 ? part[..part.LastIndexOf('.')] : part);
        }

        var known = AppDomain.CurrentDomain.GetAssemblies()
            .Where(candidate => candidate.GetName().Name!.StartsWith("Examples.Tenancy.", StringComparison.Ordinal))
            .SelectMany(candidate => candidate.GetTypes())
            .Select(type => type.FullName)
            .ToHashSet();
        var named = assembly.Migrations.Values
            .Select(migration => assembly.CreateMigration(migration, context.Database.ProviderName!).TargetModel)
            .Append(assembly.ModelSnapshot.Model)
            .SelectMany(target => target.GetEntityTypes())
            .SelectMany(entity => ClassesIn(entity.Name))
            .SelectMany(name => SampleClass().Matches(name).Select(match => match.Value))
            .Distinct()
            .ToList();

        named.Should().NotBeEmpty("{0}'s migrations name classes of the sample", module);
        named.Where(name => !known.Contains(name)).Should().BeEmpty("a class named in {0}'s migrations exists under that name", module);
    }

    /// <summary>
    /// The migrations each context finds, by the ids a database's history table records. An exported file carries
    /// its migration's id in its name, and a database made from those files holds the ids; the host compares them
    /// with these when it starts. Had one changed with the move of a migration or its context to another project,
    /// the host would call it missing on every database there is, and refuse to start.
    /// </summary>
    [Fact]
    public void The_migrations_keep_the_ids_existing_databases_hold()
    {
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Tenants"] = ["20261001215449_Initial", "20261002014829_Invitations", "20261002081230_InvitedAccounts", "20261003012526_RoleUseRemoved", "20261005214244_UnitKindAsEnum", "20261006072714_KeysFromPack"],
            ["Projects"] = ["20261001215453_Initial", "20261003012507_ProjectRoles"],
            ["Inspections"] = ["20261001215456_Initial"],
        };

        foreach (var (module, create) in DesignTimeContexts)
        {
            using var context = create();
            context.Database.GetMigrations().Should().Equal(expected[module], "{0}'s migrations keep their ids", module);
        }

        // And the files a database is made from are named by them, each with its module.
        var exported = SampleOnPostgres.MigrationFiles().Select(Path.GetFileName).ToList();
        exported.Should().Contain(expected.SelectMany(module => module.Value.Select(id => $"{id}.{module.Key.ToLowerInvariant()}.ddd.sql")));
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void The_model_names_no_table_or_column_in_lower_case(string module)
    {
        // The sample names nothing in another way for the database: tables and columns are as Entity Framework
        // names them from the classes and their properties. Naming a database another way, in snake_case for
        // one, is the host's to turn on, and would show here.
        using var context = DesignTimeContexts[module]();

        var tables = context.Model.GetRelationalModel().Tables.ToList();

        tables.Should().NotBeEmpty();
        tables.Select(table => table.Name).Should().OnlyContain(table => char.IsUpper(table[0]), "a table keeps the name of its class or the name the context gives it");
        tables.SelectMany(table => table.Columns.Select(column => column.Name))
            .Should().OnlyContain(column => char.IsUpper(column[0]), "a column keeps the name of its property");
    }

    [Fact]
    public void The_tenancy_migrations_create_tenancys_tables_and_nothing_for_its_read_model()
    {
        using var context = DesignTimeContexts["Tenants"]();

        // The rows of the read model are read from these same tables and are no tables themselves, so the
        // migrations create nothing for them: exactly the tables, and the migration history.
        TablesOf(context.Model).Should().BeEquivalentTo(TenancyTables.Select(table => $"{TenantsContext.Schema}.{table}"));
        TablesCreatedBy(context).Should().BeEquivalentTo(TablesOf(context.Model));
    }

    [Fact]
    public void A_units_kind_is_stored_by_the_key_the_rows_from_before_the_enum_hold()
    {
        using var context = DesignTimeContexts["Tenants"]();
        var kind = context.Model.FindEntityType(typeof(OrganizationUnit))!.FindProperty(nameof(OrganizationUnit.Kind))!;
        var converter = kind.GetTypeMapping().Converter!;
        var change = context.GetService<IMigrator>().GenerateScript(fromMigration: "20261003012526_RoleUseRemoved", toMigration: "20261005214244_UnitKindAsEnum");

        // The migration that made the column the enum's changes its type and nothing in it: a row written while the
        // kind was a key of the package's catalogue holds that key, and the enum is written and read by it.
        change.Should().Contain("ALTER COLUMN \"Kind\"").And.NotContain("UPDATE ");
        kind.IsNullable.Should().BeTrue("the package asks no kind, so a unit may be added without one");
        kind.GetMaxLength().Should().Be(16);
        converter.ConvertToProvider(UnitKind.Region).Should().Be("region", "a new row is written as the rows before it, and as a request spells it");
        converter.ConvertFromProvider("company").Should().Be(UnitKind.Company);
        converter.ConvertFromProvider("Site").Should().Be(UnitKind.Site, "a row written by hand reads whatever its case");
        Enum.GetValues<UnitKind>().Select(value => converter.ConvertFromProvider(converter.ConvertToProvider(value)))
            .Should().Equal(Enum.GetValues<UnitKind>().Cast<object>(), "every kind reads back as itself");
        FluentActions.Invoking(() => converter.ConvertFromProvider("galaxy")).Should().Throw<ArgumentException>("a key the enum has no member for is a row to correct");
    }

    [Fact]
    public void The_projects_migrations_create_no_tenancy_tables()
    {
        using var context = DesignTimeContexts["Projects"]();

        // Projects reads Tenancy through its functions, which Tenancy's access file makes, so nothing of Tenancy's
        // schema is in this module's migrations: not a table, not a view. They create its own tables and nothing else.
        TablesOf(context.Model).Should().BeEquivalentTo(ProjectsTables.Select(table => $"{ProjectsContext.Schema}.{table}"));
        TablesCreatedBy(context).Should().BeEquivalentTo(TablesOf(context.Model));

        context.Database.GenerateCreateScript().Should().Contain("CREATE TABLE projects.\"Projects\"").And.NotContain(ProjectsContext.TenancyTablesSchema + ".");
        context.GetService<IMigrator>().GenerateScript().Should().NotContain(ProjectsContext.TenancyTablesSchema + ".");
    }

    [Fact]
    public void The_inspections_migrations_create_only_inspections_tables()
    {
        using var context = DesignTimeContexts["Inspections"]();

        // Inspections maps no read model at all: it asks Projects' gate, and keeps only its own rows.
        TablesOf(context.Model).Should().BeEquivalentTo(InspectionsTables.Select(table => $"{InspectionsContext.Schema}.{table}"));
        TablesCreatedBy(context).Should().BeEquivalentTo(TablesOf(context.Model));
    }

    [Fact]
    public void The_first_migrations_hold_who_changed_a_row_and_the_access_history()
    {
        using var projects = (ProjectsContext)DesignTimeContexts["Projects"]();
        using var inspections = (InspectionsContext)DesignTimeContexts["Inspections"]();
        using var tenancy = DesignTimeContexts["Tenants"]();

        // Mapped before the first migration was made, so a database made from it never needs them added.
        foreach (var context in new DbContext[] { projects, inspections })
        {
            FirstMigrationOf(context).Should().ContainAll(TenancyAttribution.Columns.Select(column => $"\"{column}\""));
        }

        FirstMigrationOf(tenancy).Should().Contain($"CREATE TABLE tenancy.\"{TenantsContext.HistoryTable}\"");

        // A project keeps its number and an inspection its project: a save that would change either is refused.
        projects.Model.FindEntityType(typeof(Project))!.FindProperty(nameof(Project.Number))!.GetAfterSaveBehavior().Should().Be(PropertySaveBehavior.Throw);
        inspections.Model.FindEntityType(typeof(Inspection))!.FindProperty(nameof(Inspection.ProjectId))!.GetAfterSaveBehavior().Should().Be(PropertySaveBehavior.Throw);
    }

    /// <summary>Every table of the model, as <c>schema.Table</c>.</summary>
    private static List<string> TablesOf(IModel model)
        => [.. model.GetRelationalModel().Tables.Select(table => $"{table.Schema}.{table.Name}")];

    /// <summary>
    /// The tables a database has once every migration of the context is applied, as <c>schema.Table</c>, read from
    /// the script the migrations make: what they create and did not drop again, without the migration history,
    /// which is in the module's own schema.
    /// </summary>
    private static List<string> TablesCreatedBy(DbContext context)
    {
        var script = context.GetService<IMigrator>().GenerateScript();
        var history = $"{context.Model.GetDefaultSchema()}.{HistoryRepository.DefaultTableName}";

        var created = TableStatement().Matches(script)
            .Where(statement => statement.Groups["action"].Value == "CREATE")
            .Select(statement => $"{statement.Groups["schema"].Value}.{statement.Groups["table"].Value}")
            .ToList();
        var dropped = TableStatement().Matches(script)
            .Where(statement => statement.Groups["action"].Value == "DROP")
            .Select(statement => $"{statement.Groups["schema"].Value}.{statement.Groups["table"].Value}");

        created.Should().Contain(history, "the module keeps its migration history in its own schema");
        return [.. created.Except(dropped).Where(table => table != history)];
    }

    /// <summary>What the first migration of the context does, as the script that goes from no database to it.</summary>
    private static string FirstMigrationOf(DbContext context)
        => context.GetService<IMigrator>().GenerateScript(fromMigration: Migration.InitialDatabase, toMigration: context.Database.GetMigrations().First());

    /// <summary>A statement that makes or drops a table, with the schema and the table it names.</summary>
    [GeneratedRegex(@"^\s*(?<action>CREATE|DROP) TABLE (?:IF (?:NOT )?EXISTS )?(?<schema>\w+)\.""(?<table>\w+)""", RegexOptions.Multiline)]
    private static partial Regex TableStatement();

    /// <summary>The full name of a class of the sample, inside the name a model gives an entity type.</summary>
    [GeneratedRegex(@"Examples\.Tenancy\.[\w.]+")]
    private static partial Regex SampleClass();
}
