using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Membership.TestHost.Access;

namespace DDDToolkit.Supporting.Membership.Postgres.Tests.Infrastructure;

/// <summary>
/// The depot's own functions in the database, written by the depot and by nobody else: who the calling porter
/// is, which of its roles give a key, and the bays a porter's hold of a key reaches. They are defined under the
/// logical names the resources' rules name, which is all that joins the two: the package's functions ask these
/// by those names, and the script that writes both finds them wherever the depot's context puts them.
/// </summary>
public sealed class DepotOwnFunctions : IRowAccessContribution
{
    /// <inheritdoc />
    public string Owner => DepotFunctions.Owner;

    /// <inheritdoc />
    public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export)
    {
        if (context.Model.FindEntityType(typeof(Porter)) is not { } porters)
        {
            return null;
        }

        var roles = context.Model.FindEntityType(typeof(DepotRole))!;
        var roleKeys = context.Model.FindEntityType(typeof(DepotRoleKey))!;
        var paths = context.Model.FindEntityType(typeof(BayPath))!;
        var holds = context.Model.FindEntityType(typeof(BayHold))!;

        // Each runs as its owner and is granted to nobody: only functions that run as their owner ask them.
        return new RowAccessContributionResult(
        [
            new ContributedFunction(
                Name(DepotFunctions.CallerPorter),
                "",
                "uuid",
                $"SELECT p.{Column(porters, nameof(Porter.Id))} FROM {RowAccessModel.Table(porters)} p "
                + $"WHERE p.{Column(porters, nameof(Porter.UserId))} = {{caller:uid}} AND p.{Column(porters, nameof(Porter.Active))}",
                SecurityDefiner: true),
            new ContributedFunction(
                Name(DepotFunctions.RolesWithKey),
                "text",
                "SETOF uuid",
                $"SELECT given.{Column(roleKeys, nameof(DepotRoleKey.RoleId))} FROM {RowAccessModel.Table(roleKeys)} given "
                + $"JOIN {RowAccessModel.Table(roles)} kept ON kept.{Column(roles, nameof(DepotRole.Id))} = given.{Column(roleKeys, nameof(DepotRoleKey.RoleId))} "
                + $"WHERE given.{Column(roleKeys, nameof(DepotRoleKey.Key))} = $1 AND kept.{Column(roles, nameof(DepotRole.InUse))}",
                SecurityDefiner: true),
            new ContributedFunction(
                Name(DepotFunctions.BaysWhereIHold),
                "text",
                "SETOF uuid",
                $"SELECT path.{Column(paths, nameof(BayPath.BayId))} FROM {RowAccessModel.Table(holds)} held "
                + $"JOIN {RowAccessModel.Table(paths)} path ON path.{Column(paths, nameof(BayPath.AboveId))} = held.{Column(holds, nameof(BayHold.BayId))} "
                + $"WHERE held.{Column(holds, nameof(BayHold.PorterId))} = (SELECT {{fn:{DepotFunctions.CallerPorter}}}()) AND held.{Column(holds, nameof(BayHold.Key))} = $1",
                SecurityDefiner: true),
        ],
        [],
        []);
    }

    /// <summary>The function's own name: what comes after the owner in its logical name.</summary>
    private static string Name(string logical) => logical[(logical.IndexOf('/') + 1)..];

    private static string Column(Microsoft.EntityFrameworkCore.Metadata.IEntityType entity, string property) => RowAccessModel.Column(entity, property);
}

/// <summary>
/// The depot over one Postgres database of the calling test's own: the two kinds of resource that stand beside
/// the depot registered through the TestHost, row level security, and the depot's context connecting as the
/// login role. The data is the scenario the SQLite suite asks over, made for the moment the test runs at,
/// since the database's functions ask the database's clock.
/// </summary>
public sealed class PostgresDepot : IDisposable
{
    private const string PlainTemplate = "depot_plain";
    private const string SecuredTemplate = "depot_secured";

    private PostgresDepot(FilingDatabase database, bool asCaller)
    {
        Database = database;
        Services = new DepotServices(
            options => options.UseNpgsql(database.ConnectionString),
            TimeProvider.System,
            services => services.AddPostgresRowLevelSecurity(),
            wiring: asCaller ? null : (options, provider) => options.UseDDDToolkitCore(provider));
        Scenario = new DepotScenario(DateTimeOffset.UtcNow);
    }

    /// <summary>The depot's own row access rules, as a script is written from them.</summary>
    public static IReadOnlyList<RowAccessRule> Rules { get; } =
    [
        RowAccessRule.For<Pallet>("Porters read the pallets they are on", RowOperations.Read, PortersReadThePalletsTheyAreOn.RowAccessSql, RowAccessRoles.User),
        RowAccessRule.For<Crate>("Porters read the crates they see", RowOperations.Read, PortersReadTheCratesTheySee.RowAccessSql, RowAccessRoles.User),
    ];

    /// <summary>
    /// The contributions the access file is written with: the package's own class once for each kind of resource,
    /// as the Supabase build makes it from the rules an application marks <c>[MembershipRules&lt;TMember&gt;]</c>, and
    /// the depot's own, which the host lists with <c>UseRowAccessContribution</c>.
    /// </summary>
    public static IReadOnlyList<IRowAccessContribution> Contributions =>
    [
        new MembershipRowAccessContribution<PalletPorter>(PalletMembership.Rules),
        new MembershipRowAccessContribution<CratePorter>(CrateMembership.Rules),
        new DepotOwnFunctions(),
    ];

    /// <summary>The test's database.</summary>
    public FilingDatabase Database { get; }

    /// <summary>The depot's services.</summary>
    public DepotServices Services { get; }

    /// <summary>The data, as it is now.</summary>
    public DepotScenario Scenario { get; }

    /// <summary>The depot's context on Npgsql, for its model: it never connects.</summary>
    public static DepotContext Model() => new(new DbContextOptionsBuilder<DepotContext>().UseNpgsql("Host=model-only").Options);

    /// <summary>
    /// The access file of the depot's context, written with <paramref name="contributions"/>, or
    /// <see cref="Contributions"/>, and the depot's <paramref name="rules"/>, or its own.
    /// </summary>
    public static string AccessScript(IReadOnlyList<IRowAccessContribution>? contributions = null, IReadOnlyList<RowAccessRule>? rules = null)
    {
        using var context = Model();
        return PostgresRowAccess.Script(context, rules ?? Rules, null, new RowAccessExport { Contributions = contributions ?? Contributions });
    }

    /// <summary>
    /// The depot over a new database, with its tables and, when <paramref name="secured"/>, its access file:
    /// the functions of the two resources, the depot's own, and the policies that ask them.
    /// </summary>
    /// <param name="server">The run's Postgres.</param>
    /// <param name="secured">Whether the access file is applied.</param>
    /// <param name="asCaller">Whether the context runs every command as the caller, under the policies.</param>
    /// <param name="seed">Whether the scenario is saved.</param>
    public static async Task<PostgresDepot> CreateAsync(FilingPostgres server, bool secured = true, bool asCaller = false, bool seed = true)
    {
        var database = await server.CreateDatabaseAsync(
            secured ? SecuredTemplate : PlainTemplate,
            (template, cancellationToken) => PrepareAsync(template, secured, cancellationToken),
            TestContext.Current.CancellationToken);

        var depot = new PostgresDepot(database, asCaller);
        if (seed)
        {
            await depot.Scenario.SaveAsync(depot.Services);
            depot.Services.Commands.Reset();
        }

        return depot;
    }

    /// <summary>A session on the database as <paramref name="person"/>, past the application: what its own SQL would be answered.</summary>
    public Task<CallerSession> SessionAsync(DepotPerson person) => CallerSession.BeginAsync(Database, person.Caller);

    /// <summary>Runs <paramref name="sql"/> as the login role, which owns everything of the depot's.</summary>
    public Task ExecuteAsync(string sql) => FilingPostgres.ExecuteAsync(Database.ConnectionString, sql, TestContext.Current.CancellationToken);

    /// <summary>The first column of every row <paramref name="sql"/> answers the login role.</summary>
    public async Task<List<T>> ListAsync<T>(string sql)
    {
        await using var connection = new Npgsql.NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
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
    /// the tables made by the login role from the depot's model, with what callers may do with them, everything,
    /// so that only the policies stand in a caller's way; and the access file, for the secured one.
    /// </summary>
    private static async Task PrepareAsync(FilingDatabase template, bool secured, CancellationToken cancellationToken)
    {
        await FilingPostgres.ExecuteAsync(
            template.SuperuserConnectionString,
            $"CREATE SCHEMA ddd AUTHORIZATION {FilingPostgres.LoginRole}; CREATE SCHEMA {DepotContext.Schema} AUTHORIZATION {FilingPostgres.LoginRole};",
            cancellationToken);
        await FilingPostgres.ExecuteAsync(template.SuperuserConnectionString, PostgresRowAccess.SetupScript(new PostgresRowLevelSecurityOptions(), loginRole: FilingPostgres.LoginRole), cancellationToken);

        await using (var context = Model())
        {
            await FilingPostgres.ExecuteAsync(template.ConnectionString, context.Database.GenerateCreateScript(), cancellationToken);
        }

        await FilingPostgres.ExecuteAsync(
            template.ConnectionString,
            $"""
            GRANT USAGE ON SCHEMA {DepotContext.Schema} TO authenticated, anon, ddd_system_in;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {DepotContext.Schema} TO authenticated, anon, ddd_system_in;
            """,
            cancellationToken);

        if (secured)
        {
            await FilingPostgres.ExecuteAsync(template.ConnectionString, AccessScript(), cancellationToken);
        }
    }
}
