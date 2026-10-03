using Examples.Hosting;
using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// The sample's host on a Postgres database of the test's own, set up the way a deployment sets one up: the files
/// under <c>Examples/Tenancy/supabase/migrations</c> applied in order by the role that owns the database, the login
/// role given a password, and the host started with a connection string for that login role and nothing else.
/// </summary>
/// <remarks>
/// The database is one of <see cref="SampleSupabaseStack"/>'s, so it has the roles and the claim functions a
/// Supabase project has, and its migration role is no superuser. Nothing here helps the host along: the tables,
/// the policies, the privileges and the roles are what the exported files make, and the host migrates nothing.
/// <para>
/// The files are applied once in a run, as a deployment applies them once, to a database every test's database
/// is then a copy of. The host gets a small budget of connections: a run has dozens of hosts on the one server
/// at once, where a deployment has one or a few.
/// </para>
/// <para>
/// The demonstration is seeded once in a run as well: on a copy of that database a host is started, which seeds
/// it as every start of the sample does, the outboxes handle what the seeding stored, and the host stops. A
/// test's database is a copy of that one, and its host starts with seeding off, so it starts on the data and
/// spends no time making it. A host that has to seed itself, because the seeding is what the test is about or
/// because it runs on a clock of its own, asks for a database without the demonstration.
/// </para>
/// <para>
/// The host runs on the database's clock (<see cref="DatabaseClock"/>) unless the test gives it one of its own: it
/// stamps the start of a role, and the policies compare that with the database's <c>now()</c>.
/// </para>
/// <para>
/// Asked for with Auth, the host is also told where the stack's Auth server is, and makes the demonstration
/// people users there under <see cref="DemoPassword"/>, so a test signs one of them in for real. Auth keeps its
/// users in the stack's own database, one set for the whole run, which is why a test class that makes or changes
/// them belongs to the collection <see cref="SampleSupabaseStack.AuthUsers"/>.
/// </para>
/// <para>
/// A test reaches the data three ways: as a person, through the host's routes; as the application's own work
/// in one tenant, with <see cref="AsSystemInAsync"/>, which the policies hold to that tenant; and as the role
/// that owns the database, with <see cref="AsOwnerAsync(string, CancellationToken)"/> and
/// <see cref="AsOwnerAsync{T}"/>, which they let through.
/// </para>
/// <para>
/// A period runs out here by waiting for it: the database compares a period with its own clock, which no test
/// moves, so a test gives a role or a membership an end a few seconds ahead through the route anyone uses and
/// waits with <see cref="WaitUntilItIsPastAsync"/> before it asks again.
/// </para>
/// </remarks>
public sealed class SampleOnPostgres : IAsyncDisposable
{
    /// <summary>The role the host logs in as, which the hand-written migration makes without a login.</summary>
    public const string LoginRole = "tenancy_api";

    /// <summary>
    /// The login role's password for this run. Roles are the server's, not a database's, so every database of the
    /// run shares the one role, and one password.
    /// </summary>
    private static readonly string LoginPassword = Guid.NewGuid().ToString("N");

    /// <summary>
    /// The password the demonstration people sign in with at the stack's Auth server, for this run. Auth has one
    /// set of users for the run, so every host that makes them gives them this one.
    /// </summary>
    public static string DemoPassword { get; } = "Pw-" + Guid.NewGuid().ToString("N");

    /// <summary>The database the exported files are applied to, once in a run: what a host that seeds itself starts on a copy of.</summary>
    private const string Migrated = "tenancy_migrated";

    /// <summary>
    /// A copy of <see cref="Migrated"/> with the demonstration in it, seeded once in a run by a host that then
    /// stopped: what every other host starts on a copy of.
    /// </summary>
    private const string Seeded = "tenancy_seeded";

    /// <summary>
    /// The connections a test's host may hold: a few for requests, which a scenario sends one or two at a time,
    /// and two for the three outbox pollers, which take turns.
    /// </summary>
    private static readonly Dictionary<string, string> Budget = new()
    {
        [SampleStorage.PoolsSection + ":" + nameof(PostgresPoolBudget.Requests)] = "4",
        [SampleStorage.PoolsSection + ":" + nameof(PostgresPoolBudget.Background)] = "2",
    };

    /// <summary>The hosts <see cref="StartAnotherHostAsync"/> started, which stop with this one.</summary>
    private readonly List<SampleFactory> _others = [];

    private SampleOnPostgres(SupabaseDatabase database, SampleFactory host, SupabaseAuthServer? auth)
    {
        Database = database;
        Host = host;
        Auth = auth;
    }

    /// <summary>The database, with a connection string for the role that owns it.</summary>
    public SupabaseDatabase Database { get; }

    /// <summary>The stack's Auth server, when the host was asked for with it; <see langword="null"/> otherwise.</summary>
    public SupabaseAuthServer? Auth { get; }

    /// <summary>The host, in Development unless it was asked for in another environment, logged in as <see cref="LoginRole"/>.</summary>
    public SampleFactory Host { get; }

    /// <summary>A connection string to the database for the role the host logs in as.</summary>
    public string AsLoginRole => Database.As(LoginRole, LoginPassword);

    /// <summary>
    /// A database with every exported file applied, the login turned on and the demonstration seeded, and the
    /// host on it, not yet started: it starts with the first client or the first look at its services. Skips the
    /// test without Docker, or fails it where containers are required.
    /// </summary>
    /// <param name="stack">The run's stack.</param>
    /// <param name="cancellationToken">Cancels the set-up.</param>
    /// <param name="connectAs">The connection string the host is given, from the database; the login role's when left out.</param>
    /// <param name="withAuth">
    /// Whether the host knows the stack's Auth server and makes the demonstration people users there, each under
    /// their fixed id, with <see cref="DemoPassword"/>.
    /// </param>
    /// <param name="services">Changes to the host's services, made after its own registrations.</param>
    /// <param name="settings">More settings for the host, which win over the ones made here.</param>
    /// <param name="environment">The environment the host runs in; Development when left out.</param>
    /// <param name="seeded">
    /// Whether the database holds the demonstration already, as the run's one seeding left it, and the host
    /// starts with seeding off. <see langword="false"/> for a database with the exported files applied and
    /// nothing in it, on which the host does what its environment and its settings say: in Development it seeds
    /// the demonstration itself, under its own clock, before it answers. A host that is given a clock of its own
    /// is asked for that way, since what it seeds has to carry that clock's moments.
    /// </param>
    public static async Task<SampleOnPostgres> CreateAsync(
        SampleSupabaseStack stack,
        CancellationToken cancellationToken,
        Func<SupabaseDatabase, string>? connectAs = null,
        bool withAuth = false,
        Action<IServiceCollection>? services = null,
        IReadOnlyDictionary<string, string>? settings = null,
        string? environment = null,
        bool seeded = true)
    {
        ArgumentNullException.ThrowIfNull(stack);

        var database = seeded
            ? await stack.CreateDatabaseFromAsync(Seeded, SeedAsync, cancellationToken, startingFrom: await stack.FilledAsync(Migrated, MigrateAsync, cancellationToken))
            : await stack.CreateDatabaseFromAsync(Migrated, MigrateAsync, cancellationToken);
        var auth = withAuth ? await stack.AuthAsync(cancellationToken) : null;

        var made = SettingsFor(connectAs?.Invoke(database) ?? database.As(LoginRole, LoginPassword));
        if (seeded)
        {
            made[DemoSeeder.Setting] = "false";
        }

        if (auth is not null)
        {
            // Where Auth answers, with nothing in front of it, and what its admin API takes there. The issuer the
            // host checks tokens against stays the one of its development settings, which this Auth server signs with.
            made[SampleAuthentication.AuthUrlSetting] = auth.Url.ToString();
            made[DemoAuthUsers.SecretKeySetting] = auth.ServiceRoleKey;
            made[DemoAuthUsers.Setting] = "true";
            made[DemoAuthUsers.PasswordSetting] = DemoPassword;
        }

        foreach (var (name, value) in settings ?? new Dictionary<string, string>())
        {
            made[name] = value;
        }

        var host = await HostOnAsync(database, environment ?? Environments.Development, made, services, seedsItself: !seeded, cancellationToken);
        return new SampleOnPostgres(database, host, auth);
    }

    /// <summary>The settings every host of a run is given: its connection string, and its budget of connections.</summary>
    private static Dictionary<string, string> SettingsFor(string connectionString)
        => new(Budget) { ["ConnectionStrings:" + SampleStorage.ConnectionString] = connectionString };

    /// <summary>
    /// Another host on the same database, started: what the next start of the application is, on the data the
    /// first one left. It runs in Development with seeding as the settings files have it, so it seeds the
    /// demonstration when it finds none, and says so when it finds part of one. It stops with this sample, or
    /// when the test disposes it.
    /// </summary>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <param name="services">Changes to its services, made after the host's own registrations.</param>
    /// <param name="settings">More settings for the host, which win over the ones made here.</param>
    public async Task<SampleFactory> StartAnotherHostAsync(
        CancellationToken cancellationToken,
        Action<IServiceCollection>? services = null,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        var made = SettingsFor(AsLoginRole);
        foreach (var (name, value) in settings ?? new Dictionary<string, string>())
        {
            made[name] = value;
        }

        var host = await HostOnAsync(Database, Environments.Development, made, services, seedsItself: true, cancellationToken);
        _others.Add(host);
        _ = host.Server;
        return host;
    }

    /// <summary>
    /// The host on <paramref name="database"/>, not yet started, on the database's clock unless
    /// <paramref name="services"/> registers another, which only a host that seeds itself may.
    /// </summary>
    private static async Task<SampleFactory> HostOnAsync(
        SupabaseDatabase database,
        string environment,
        IReadOnlyDictionary<string, string> settings,
        Action<IServiceCollection>? services,
        bool seedsItself,
        CancellationToken cancellationToken)
    {
        // Registered before the test's own changes, so a clock the test registers is the one the host gets.
        var clock = await DatabaseClock.OfAsync(database.AsMigrationRole).WaitAsync(cancellationToken);
        void WithTheDatabasesClock(IServiceCollection collection)
        {
            collection.AddSingleton<TimeProvider>(clock);
            services?.Invoke(collection);

            // The demonstration's periods are measured from the moment it was seeded, by the clock of the host
            // that seeded it. A host on another clock would compare them with moments of its own.
            if (!seedsItself && collection.Last(registered => registered is { IsKeyedService: false } && registered.ServiceType == typeof(TimeProvider)).ImplementationInstance != clock)
            {
                throw new InvalidOperationException(
                    "A host that is given a clock of its own seeds the demonstration itself, under that clock: ask for it with seeded: false.");
            }
        }

        return SampleFactory.In(environment, settings, WithTheDatabasesClock);
    }

    /// <summary>
    /// What a deployment does to a new project's database, once: every file of the directory applied as the
    /// Supabase CLI applies them, in the order of their names, each as it is, and then, as the owner, the login
    /// turned on. No migration carries a password.
    /// </summary>
    private static async Task MigrateAsync(SupabaseDatabase database)
    {
        foreach (var file in MigrationFiles())
        {
            await SampleSupabaseStack.ExecuteAsync(database.AsMigrationRole, await File.ReadAllTextAsync(file), CancellationToken.None);
        }

        await SampleSupabaseStack.ExecuteAsync(database.AsMigrationRole, $"ALTER ROLE {LoginRole} WITH LOGIN PASSWORD '{LoginPassword}'", CancellationToken.None);
    }

    /// <summary>
    /// What makes <see cref="Seeded"/> of a copy of <see cref="Migrated"/>, once: a host in Development on the
    /// database itself, which seeds the demonstration before it answers, as every start of the sample does. It
    /// stops only when every module's outbox has handled what the seeding stored, so no copy starts with events
    /// its own pollers would still send, and it leaves nobody connected, or no copy could be made.
    /// </summary>
    private static async Task SeedAsync(SupabaseDatabase database)
    {
        var host = await HostOnAsync(
            database, Environments.Development, SettingsFor(database.As(LoginRole, LoginPassword)), services: null, seedsItself: true, CancellationToken.None);
        await using (host)
        {
            try
            {
                _ = host.Server;
            }
            catch (Exception exception)
            {
                // What is thrown here is not always the reason: the host's own log has it, written before it gave up.
                throw new InvalidOperationException(
                    $"The host that seeds the demonstration once for the run did not start. It logged:{Environment.NewLine}{host.Errors.Describe()}", exception);
            }

            await Outboxes.DrainedAsync(host, CancellationToken.None);
        }

        await WaitUntilNobodyIsConnectedAsync(database);
    }

    /// <summary>
    /// Waits until the connections of the host that stopped are gone from the server: a connection that was closed
    /// here is there for a moment longer, and Postgres copies no database anybody is connected to. Failing here
    /// fails the run once, with who is still there; every copy would otherwise fail on its own.
    /// </summary>
    private static async Task WaitUntilNobodyIsConnectedAsync(SupabaseDatabase database)
    {
        const string Others = """
            SELECT string_agg(usename || ' (' || coalesce(nullif(application_name, ''), 'unnamed') || ')', ', ')
            FROM pg_stat_activity
            WHERE datname = current_database() AND pid <> pg_backend_pid()
            """;

        object? connected = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using (var connection = new NpgsqlConnection(database.AsMigrationRole))
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand(Others, connection);
                connected = await command.ExecuteScalarAsync();
            }

            if (connected is null or DBNull)
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException(
            $"The database '{database.Name}' still has connections ten seconds after the host that seeded it stopped, so no copy of it can be made: {connected}.");
    }

    /// <summary>The exported files and the hand-written one, in the order they are applied.</summary>
    public static IReadOnlyList<string> MigrationFiles()
        => [.. Directory.GetFiles(Path.Combine(SampleLayout.RepositoryRoot(), "Examples", "Tenancy", "supabase", "migrations"), "*.sql")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)];

    /// <summary>
    /// Waits until the database's clock has passed <paramref name="moment"/> of the application's. A host stamps
    /// what it writes with its own clock and the policies ask the database's, so on a machine whose container runs
    /// a moment behind, a grant made just now is not yet in force for the database. Where the two clocks agree it
    /// is one question.
    /// </summary>
    public async Task WaitUntilTheDatabaseIsPastAsync(DateTimeOffset moment, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(Database.AsMigrationRole);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT now()", connection);

        for (var attempt = 0; attempt < 300; attempt++)
        {
            var now = (DateTime)(await command.ExecuteScalarAsync(cancellationToken))!;
            if (new DateTimeOffset(now.ToUniversalTime(), TimeSpan.Zero) > moment)
            {
                return;
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException($"The database's clock is still before {moment:O} after thirty seconds: the container's clock is too far behind this machine's.");
    }

    /// <summary>
    /// Waits until <paramref name="moment"/> is past for the database and for the host alike: a period that ends
    /// then has run out, whoever compares it. The policies ask the database's clock and the application asks the
    /// host's, which runs a moment behind the database's at most, so the database is waited for first and the
    /// host's clock, which is seldom still short of it, after.
    /// </summary>
    /// <param name="moment">The moment a period ends, by the host's clock.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    public async Task WaitUntilItIsPastAsync(DateTimeOffset moment, CancellationToken cancellationToken)
    {
        await WaitUntilTheDatabaseIsPastAsync(moment, cancellationToken);

        // A moment at most on a clock that runs. One that stands still never gets there, and says so.
        var clock = Host.Services.GetRequiredService<TimeProvider>();
        for (var attempt = 0; clock.GetUtcNow() <= moment; attempt++)
        {
            if (attempt == 500)
            {
                throw new TimeoutException(
                    $"The host's clock is still before {moment:O} ten seconds after the database's passed it: a period runs out only on a host whose clock runs.");
            }

            await Task.Delay(20, cancellationToken);
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> as the application's own work inside <paramref name="tenant"/>, acting for its
    /// first administrator, in a scope of the host's services of its own: the way the host seeds a tenant. On
    /// Postgres that is a database role of its own, which the policies hold to the one tenant.
    /// </summary>
    public async Task<T> AsSystemInAsync<T>(DemoTenant tenant, Func<IServiceProvider, Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(work);

        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(tenant.Id, tenant.Administrator.Id))
        {
            await using var scope = Host.Services.CreateAsyncScope();
            return await work(scope.ServiceProvider);
        }
    }

    /// <summary>Runs <paramref name="sql"/> as the role that owns the database, which the forced policies let through.</summary>
    public Task AsOwnerAsync(string sql, CancellationToken cancellationToken)
        => SampleSupabaseStack.ExecuteAsync(Database.AsMigrationRole, sql, cancellationToken);

    /// <summary>
    /// What <paramref name="sql"/> answers to the role that owns the database: the rows as they are stored, past
    /// every policy, which no caller of the application reads. A count asked this way is of every tenant's rows.
    /// </summary>
    /// <typeparam name="T">The type of the one value the statement answers.</typeparam>
    public async Task<T> AsOwnerAsync<T>(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(Database.AsMigrationRole);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>
    /// Keys no crew gives, which the application refuses to put on a project role: naming an owner, opening
    /// projects, and managing seats and grants.
    /// </summary>
    public static IReadOnlyList<string> KeysNoCrewGives { get; } =
        [ProjectKeys.ChangeOwner, ProjectKeys.Open, DDDToolkit.Supporting.Tenancy.Catalogue.TenancyKeys.SeatsManage, DDDToolkit.Supporting.Tenancy.Catalogue.TenancyKeys.GrantsManage];

    /// <summary>
    /// Writes <see cref="KeysNoCrewGives"/> into a project role's row beside its own keys, as the role that owns the
    /// database: what a role manager's own statement could write past the application. Whoever holds the role on a
    /// crew is then cut to what a crew gives by the questions and by the projects' functions alone, which is what a
    /// test of that cut needs.
    /// </summary>
    public Task WidenProjectRoleAsync(ProjectRoleId role, CancellationToken cancellationToken)
        => AsOwnerAsync(
            $"""
            UPDATE projects."{ProjectsContext.ProjectRolesTable}"
            SET "Keys" = "Keys" || ARRAY[{string.Join(", ", KeysNoCrewGives.Select(key => "'" + key + "'"))}]::text[]
            WHERE "Id" = '{role.Value}'
            """,
            cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var other in _others)
        {
            await other.DisposeAsync();
        }

        await Host.DisposeAsync();
    }
}
