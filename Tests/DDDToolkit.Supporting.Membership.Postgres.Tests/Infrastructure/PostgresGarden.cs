using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Membership.TestHost.Access;
using Npgsql;

namespace DDDToolkit.Supporting.Membership.Postgres.Tests.Infrastructure;

/// <summary>The row access contribution of the plots: the application's class, closed over its member class, with its rules.</summary>
public sealed class PlotMembershipFunctions() : MembershipRowAccessContribution<PlotGardener>(PlotMembership.Rules);

/// <summary>The row access contribution of the sheds, next to the plots': one for each kind of resource.</summary>
public sealed class ShedMembershipFunctions() : MembershipRowAccessContribution<ShedHand>(ShedMembership.Rules);

/// <summary>
/// The gardens' own function in the database, written by the host and by nobody else: the garden the caller's
/// token says its requests are in, which the host's rule on the roles of plots asks. It reads the caller's
/// claims alone, so it runs as the caller, and a signed-in user may ask it, as that rule is for one.
/// </summary>
public sealed class GardenOwnFunctions : IRowAccessContribution
{
    /// <inheritdoc />
    public string Owner => "gardens";

    /// <inheritdoc />
    public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export)
        => context.Model.FindEntityType(typeof(PlotRole)) is null
            ? null
            : new RowAccessContributionResult(
                [new ContributedFunction("caller_garden", "", "uuid", $"SELECT ({{caller:claim:{TestCallers.GardenClaim}}})::pg_catalog.uuid", GrantTo: [RowAccessRoles.User])],
                [],
                []);
}

/// <summary>
/// The gardens over one Postgres database of the calling test's own: the plots and the sheds registered through
/// the TestHost, row level security, and the gardens' context connecting as the login role. The data is the
/// scenario the SQLite suite asks over, made for the moment the test runs at, since the database's functions
/// ask the database's clock.
/// </summary>
public sealed class PostgresGarden : IDisposable
{
    private const string PlainTemplate = "gardens_plain";
    private const string SecuredTemplate = "gardens_secured";

    private PostgresGarden(FilingDatabase database, bool asCaller, Action<IServiceCollection>? configure, MembershipRules? sheds)
    {
        Database = database;
        Services = new GardenServices(
            options => options.UseNpgsql(database.ConnectionString),
            TimeProvider.System,
            services =>
            {
                services.AddPostgresRowLevelSecurity();
                configure?.Invoke(services);
            },
            wiring: asCaller ? (options, provider) => options.UsePostgresRowLevelSecurity(provider) : null,
            sheds: sheds);
        Scenario = new GardenScenario(DateTimeOffset.UtcNow);
    }

    /// <summary>The gardens' own row access rules, as a script is written from them.</summary>
    public static IReadOnlyList<RowAccessRule> Rules { get; } =
    [
        RowAccessRule.For<Plot>("Gardeners read the plots they are on", RowOperations.Read, GardenersReadThePlotsTheyAreOn.RowAccessSql, RowAccessRoles.User),

        // The roles' own tables: read by a signed-in user, those of plots in its own garden, written by nobody but the host's own work.
        RowAccessRule.For<PlotRole>("Gardeners read the roles of their garden", RowOperations.Read, GardenersReadTheRolesOfTheirGarden.RowAccessSql, RowAccessRoles.User),
        RowAccessRule.For<ShedRole>("Users read the roles of sheds", RowOperations.Read, UsersReadTheRolesOfSheds.RowAccessSql, RowAccessRoles.User),

        // Whoever fences a plot changes it, its gardeners and their roles; the package's lock holds those rows to the same key.
        RowAccessRule.For<Plot>("Fencers change their plots", RowOperations.Change, FencersChangeTheirPlots.RowAccessSql, RowAccessRoles.User),
    ];

    /// <summary>
    /// The contributions the host lists: one for each kind of resource, and the host's own function its rule on
    /// the roles of plots asks. The roles need none of the package's: they are read where they are kept.
    /// </summary>
    public static IReadOnlyList<IRowAccessContribution> Contributions => [new PlotMembershipFunctions(), new ShedMembershipFunctions(), new GardenOwnFunctions()];

    /// <summary>The test's database.</summary>
    public FilingDatabase Database { get; }

    /// <summary>The gardens' services.</summary>
    public GardenServices Services { get; }

    /// <summary>The data, as it is now.</summary>
    public GardenScenario Scenario { get; }

    /// <summary>The gardens' context on Npgsql, for its model: it never connects.</summary>
    public static GardenContext Model() => new(new DbContextOptionsBuilder<GardenContext>().UseNpgsql("Host=model-only").Options);

    /// <summary>
    /// The access file of the gardens' context, written with <paramref name="contributions"/>, or the two the
    /// host lists, and the gardens' <paramref name="rules"/>, or its own.
    /// </summary>
    public static string AccessScript(IReadOnlyList<IRowAccessContribution>? contributions = null, IReadOnlyList<RowAccessRule>? rules = null)
    {
        using var context = Model();
        return PostgresRowAccess.Script(context, rules ?? Rules, null, new RowAccessExport { Contributions = contributions ?? Contributions });
    }

    /// <summary>
    /// The gardens over a new database, with their tables and, when <paramref name="secured"/>, their access
    /// file: the functions of the plots and of the sheds, and the policy that asks the plots'.
    /// </summary>
    /// <param name="server">The run's Postgres.</param>
    /// <param name="secured">Whether the access file is applied.</param>
    /// <param name="asCaller">Whether the context runs every command as the caller, under the policies.</param>
    /// <param name="seed">Whether the scenario is saved.</param>
    /// <param name="configure">Registers what a test needs before the resources are registered.</param>
    /// <param name="sheds">Other rules for the sheds than the host's own, for a test that registers them otherwise.</param>
    public static async Task<PostgresGarden> CreateAsync(
        FilingPostgres server,
        bool secured = true,
        bool asCaller = false,
        bool seed = true,
        Action<IServiceCollection>? configure = null,
        MembershipRules? sheds = null)
    {
        var database = await server.CreateDatabaseAsync(
            secured ? SecuredTemplate : PlainTemplate,
            (template, cancellationToken) => PrepareAsync(template, secured, cancellationToken),
            TestContext.Current.CancellationToken);

        var garden = new PostgresGarden(database, asCaller, configure, sheds);
        if (seed)
        {
            await garden.Scenario.SaveAsync(garden.Services);
            garden.Services.Commands.Reset();
            garden.Services.Raised.Clear();
        }

        return garden;
    }

    /// <summary>A session on the database as <paramref name="person"/>, past the application: what its own SQL would be answered.</summary>
    public Task<CallerSession> SessionAsync(GardenPerson person) => CallerSession.BeginAsync(Database, person.Caller);

    /// <summary>Runs <paramref name="sql"/> as the login role, which owns everything of the gardens'.</summary>
    public Task ExecuteAsync(string sql) => FilingPostgres.ExecuteAsync(Database.ConnectionString, sql, TestContext.Current.CancellationToken);

    /// <summary>
    /// The first column of every row <paramref name="sql"/> answers the login role, with <paramref name="parameters"/>
    /// bound in order: past every policy, since that role owns the tables, and held by the database's triggers.
    /// </summary>
    public async Task<List<T>> ListAsync<T>(string sql, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = parameter });
        }

        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(reader.GetFieldValue<T>(0));
        }

        return rows;
    }

    /// <inheritdoc />
    public void Dispose() => Services.Dispose();

    /// <summary>
    /// A template: its schemas made by the superuser for the login role, the setup script run by the superuser,
    /// the tables made by the login role from the gardens' model, with what callers may do with them, everything,
    /// so that only the policies stand in a caller's way; and the access file, for the secured one.
    /// </summary>
    private static async Task PrepareAsync(FilingDatabase template, bool secured, CancellationToken cancellationToken)
    {
        await FilingPostgres.ExecuteAsync(
            template.SuperuserConnectionString,
            $"CREATE SCHEMA ddd AUTHORIZATION {FilingPostgres.LoginRole}; CREATE SCHEMA {GardenContext.Schema} AUTHORIZATION {FilingPostgres.LoginRole};",
            cancellationToken);
        await FilingPostgres.ExecuteAsync(template.SuperuserConnectionString, PostgresRowAccess.SetupScript(new PostgresRowLevelSecurityOptions(), loginRole: FilingPostgres.LoginRole), cancellationToken);

        await using (var context = Model())
        {
            await FilingPostgres.ExecuteAsync(template.ConnectionString, context.Database.GenerateCreateScript(), cancellationToken);
        }

        await FilingPostgres.ExecuteAsync(
            template.ConnectionString,
            $"""
            GRANT USAGE ON SCHEMA {GardenContext.Schema} TO authenticated, anon, ddd_system_in;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {GardenContext.Schema} TO authenticated, anon, ddd_system_in;
            """,
            cancellationToken);

        if (secured)
        {
            await FilingPostgres.ExecuteAsync(template.ConnectionString, AccessScript(), cancellationToken);
        }
    }
}
