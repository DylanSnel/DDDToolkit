using System.Text.RegularExpressions;
using System.Xml.Linq;
using DDDToolkit.EntityFramework.Supabase;
using Examples.Hosting;
using Examples.Tenancy.Tenants.Contracts.TokenRoles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Architecture;

/// <summary>
/// How the sample is put together for Postgres, held to what its project files and its source say: the export is
/// a program of its own that nothing references, the host exports nothing, the connections are the host's alone,
/// and the roles the policies are exported for are the roles the host runs as. And Postgres is the one database:
/// a host without its connection string stops, a module is registered on the host's connections or not at all,
/// and the migrations a running host checks are the ones its modules' design-time factories find.
/// </summary>
public sealed partial class PostgresCompositionTests
{
    private static readonly string ExporterFile = SampleLayout.ProgramProjectFile(SampleLayout.Exporter);

    [Fact]
    public void Nothing_references_the_exporter()
    {
        // A program without routes: it references the modules' storage, which no other program of the sample may.
        // Whatever referenced it would have that within reach, and would run its export with every build.
        var root = SampleLayout.RepositoryRoot();
        var referencing = new[] { "Examples", "Tests" }
            .SelectMany(folder => Directory.GetFiles(Path.Combine(root, folder), "*.csproj", SearchOption.AllDirectories))
            .Where(file => ReferencesOf(file).Contains(SampleLayout.Exporter))
            .Select(Path.GetFileNameWithoutExtension);

        File.Exists(ExporterFile).Should().BeTrue("the exporter is at {0}", ExporterFile);
        referencing.Should().BeEmpty();
    }

    [Fact]
    public void The_exporter_references_every_modules_infrastructure_project_and_the_catalogue()
    {
        // A module's migrations and its row access rules are its infrastructure project's, with the factory the
        // export builds its context with.
        var storage = SampleLayout.Projects.Where(project => project.Layer == Layer.Infrastructure).Select(project => project.Name).ToList();

        storage.Should().HaveCount(SampleLayout.Modules.Count, "every module has an infrastructure project, which holds its migrations");
        ReferencesOf(ExporterFile).Where(reference => reference.StartsWith("Examples.Tenancy.", StringComparison.Ordinal))
            .Should().BeEquivalentTo([.. storage, SampleLayout.Catalogue], "a module the exporter does not reference is a module whose tables no exported file makes, and it exports from nothing else of the sample");
    }

    /// <summary>
    /// The host has one database and no fallback. Started with no connection string, as <c>dotnet run</c> in its
    /// folder starts it, it stops before it is built, and what it throws says the two ways to a database it can
    /// run on. A host that started anyway would have to store somewhere without the roles and the policies.
    /// </summary>
    [Fact]
    public async Task The_host_stops_without_a_connection_string_and_says_how_to_get_one()
    {
        await using var host = SampleFactory.WithoutAConnectionString();

        var refused = host.RefusedStart();

        refused.Should().ContainSingle(failure => failure is InvalidOperationException && failure.Message == SampleStorage.NoConnectionString, "the missing setting is what stops the host, and it gave up with {0}", string.Join(" / ", refused.Select(failure => failure.Message)))
            .Which.Message.Should().Contain("ConnectionStrings:" + SampleStorage.ConnectionString, "it names the setting")
            .And.Contain("Examples.Tenancy.AppHost", "the AppHost starts Supabase's images and sets it")
            .And.Contain("supabase start", "the Supabase CLI's stack is the other way")
            .And.Contain(SampleOnPostgres.LoginRole, "and the role the host logs in as there");
    }

    /// <summary>
    /// A module has no second way to register its context: handed a host that was not built on the host's
    /// connections to Postgres, each module's registration refuses, and says how such a host is built. A module
    /// that registered anyway would run on a database without the roles and the policies its rules rest on.
    /// </summary>
    [Fact]
    public void A_module_is_registered_on_the_hosts_connections_to_postgres_or_refused()
    {
        var withoutPools = ModuleHost.InProcess(ModuleDatabase.Supabase("Host=unused"));
        var registrations = new Dictionary<string, Action<IServiceCollection>>
        {
            ["Tenants"] = services => services.AddTenantsInfrastructure(withoutPools, SampleCatalogue.Application),
            ["Projects"] = services => services.AddProjectsInfrastructure(withoutPools, SampleCatalogue.Projects),
            ["Inspections"] = services => services.AddInspectionsInfrastructure(withoutPools),
        };

        registrations.Keys.Should().BeEquivalentTo(SampleLayout.Modules, "every module of the sample is asked");
        foreach (var (module, register) in registrations)
        {
            var services = new ServiceCollection();

            register.Invoking(registration => registration(services)).Should().Throw<InvalidOperationException>("{0} runs on Postgres and on nothing else", module)
                .WithMessage("*ModuleHost.OnPostgres*");
            services.Should().BeEmpty("{0} registers nothing on a host it cannot run on", module);
        }
    }

    /// <summary>
    /// The host applies no migration: at start-up it asks each running context which of its migrations the
    /// database misses, and stops when one does. So a running context has to find the migrations its module's
    /// design-time factory finds, the ones <c>dotnet ef</c> writes and the export turns into files, and look for
    /// their history in the same table. A running context that found none would call an empty database complete.
    /// Nothing names an assembly to either: both read the context's own, where the migrations are.
    /// </summary>
    [Fact]
    public async Task The_running_host_checks_the_migrations_the_design_time_factories_find()
    {
        await using var host = SampleFactory.WithoutDatabase();
        await using var scope = host.Services.CreateAsyncScope();
        var sources = host.Services.GetSupabaseMigrationSources();

        sources.Select(source => source.ContextType).Should().BeEquivalentTo(
            [typeof(TenantsContext), typeof(ProjectsContext), typeof(InspectionsContext)],
            "the start-up check asks every module's context");
        foreach (var source in sources)
        {
            using var designTime = source.CreateDesignTimeContext();
            var running = (DbContext)scope.ServiceProvider.GetRequiredService(source.ContextType);

            running.Database.GetMigrations().Should().NotBeEmpty("the host's {0} finds its module's migrations", source.ContextType.Name)
                .And.Equal(designTime.Database.GetMigrations(), "the host is checked against what dotnet ef and the export read");

            var (atRunTime, atDesignTime) = (RelationalOptionsExtension.Extract(running.GetService<IDbContextOptions>()), RelationalOptionsExtension.Extract(designTime.GetService<IDbContextOptions>()));
            atRunTime.MigrationsAssembly.Should().BeNull("the migrations are in the assembly of {0}", source.ContextType.Name);
            atDesignTime.MigrationsAssembly.Should().BeNull();
            (atRunTime.MigrationsHistoryTableSchema, atRunTime.MigrationsHistoryTableName)
                .Should().Be((atDesignTime.MigrationsHistoryTableSchema, atDesignTime.MigrationsHistoryTableName), "both look for the history in one table");
            atRunTime.MigrationsHistoryTableSchema.Should().Be(running.Model.GetDefaultSchema(), "which is in the module's own schema");
        }
    }

    [Fact]
    public void The_host_runs_no_export()
    {
        var host = XDocument.Load(SampleLayout.HostProjectFile());

        host.Descendants().Select(element => element.Name.LocalName)
            .Should().NotContain(name => name.StartsWith("Supabase", StringComparison.Ordinal), "the host sets none of the export's properties: the exporter does");
        host.Descendants("Import").Should().BeEmpty("the build step that exports is imported by the exporter alone");
        ReferencesOf(SampleLayout.HostProjectFile()).Should().NotContain(reference => reference.Contains("Supabase.Analyzers", StringComparison.Ordinal));

        // The exporter, on the other hand, exports, and only checks where the build is a continuous integration's.
        var exporter = XDocument.Load(ExporterFile);
        exporter.Descendants("SupabaseMigrationsExport").Select(element => (element.Value, Condition: element.Attribute("Condition")?.Value))
            .Should().Equal(("Write", null), ("Check", "'$(ContinuousIntegrationBuild)' == 'true'"));
    }

    [Fact]
    public void The_policies_are_exported_for_the_roles_the_host_runs_as()
    {
        // The export runs before any configuration is read, so its roles are a property of the exporter's project,
        // and the host's are code. Nothing but this keeps the two the same until the host's start-up check
        // compares the database with them.
        var roles = XDocument.Load(ExporterFile).Descendants("SupabaseRowAccessRoles").Single().Value.Split('|');

        roles.Should().Contain("system=" + SampleStorage.BookkeepingRole)
            .And.Contain($"token:{SampleTokenRoles.Operator}={SampleTokenRoles.Operator}")
            .And.Contain(["user=authenticated", "anonymous=anon", "system-in=ddd_system_in"]);

        var exporter = XDocument.Load(ExporterFile);
        exporter.Descendants("SupabaseRowAccessGrants").Single().Value.Should().Be("Write", "the privileges are written from the policies, never by hand");
        exporter.Descendants("SupabaseForceRowLevelSecurity").Single().Value.Should().Be("true", "the tables' owner is held to the policies too");
        exporter.Descendants("SupabaseLoginRole").Single().Value.Should().Be(SampleOnPostgres.LoginRole, "the role the host logs in as is made by the export, a member of these roles and of nothing else");
    }

    [Fact]
    public void No_module_opens_connections_of_its_own()
    {
        // The host's two data sources bound what it holds on the database. A module that made a data source, or
        // named a connection string, would hold connections nobody budgeted. The design-time factories connect to
        // nothing, and take their provider from the shared hosting project.
        var modules = Path.Combine(SampleLayout.RepositoryRoot(), "Examples", "Tenancy", "Modules");
        string[] forbidden = ["NpgsqlDataSource", "UseNpgsql(", "NpgsqlConnection", "GetConnectionString"];

        var offending = SampleLayout.SourceFilesIn(modules)
            .Where(file => !file.Contains("/Migrations/", StringComparison.Ordinal))
            .Where(file => forbidden.Any(File.ReadAllText(Path.Combine(modules, file)).Contains));

        offending.Should().BeEmpty("a module registers its context on the connections the host hands it");
    }

    /// <summary>
    /// An exported access file names the project a row access contribution is in, with that project's version. So
    /// such a project has a version of its own, and keeps it against the one a release build gives every project
    /// on the command line. With the toolkit's version there, a release would find the access file stale with
    /// nothing in it changed but that number; and a release build only compares the files, so it would fail.
    /// </summary>
    [Fact]
    public void A_project_an_exported_access_file_names_keeps_a_version_of_its_own()
    {
        var exported = Path.Combine(SampleLayout.RepositoryRoot(), "Examples", "Tenancy", "supabase", "migrations");
        var named = Directory.GetFiles(exported, "*_access.*.sql")
            .SelectMany(file => ContributionProject().Matches(File.ReadAllText(file)))
            .Select(match => (Project: match.Groups["project"].Value, Version: match.Groups["version"].Value))
            .Distinct()
            .ToList();

        named.Select(project => project.Project).Should().BeEquivalentTo(
            [SampleLayout.Catalogue, SampleLayout.Project("Projects", Layer.Infrastructure).Name],
            "Tenancy's contribution is used from the catalogue's project, and Projects holds a trigger of its own beside its row rules");
        foreach (var (project, version) in named)
        {
            var file = XDocument.Load(SampleLayout.SampleProjectFiles().Single(path => Path.GetFileNameWithoutExtension(path) == project));

            file.Descendants("Version").Select(element => element.Value).Should().Equal([version], "{0} has the version the exported files name it with", project);
            (file.Root!.Attribute("TreatAsLocalProperty")?.Value).Should().Be("Version", "{0} keeps its version when a build gives another with -p:Version", project);
        }
    }

    /// <summary>The names of the projects <paramref name="projectFile"/> references.</summary>
    private static List<string> ReferencesOf(string projectFile)
        => [.. XDocument.Load(projectFile).Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value.Replace('\\', '/')))];

    /// <summary>Where an exported access file says which project a row access contribution is in, and at which version.</summary>
    [GeneratedRegex(@"row access contribution [\w.]+ in (?<project>[\w.]+) (?<version>\d+\.\d+\.\d+)")]
    private static partial Regex ContributionProject();
}
