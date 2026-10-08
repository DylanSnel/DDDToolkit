using System.Collections.Concurrent;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.EventLog;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// The apiary with an event log next to its outbox: every event of a hive is kept for thirty days, on a row that
/// also says which yard the hive stands in, a column of the module's own.
/// </summary>
public sealed class LoggedApiaryContext(DbContextOptions options) : ApiaryContext(options)
{
    /// <summary>How long the apiary keeps a row of its log.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);

    /// <summary>The column the apiary adds to its log.</summary>
    public const string Yard = "Yard";

    public DbSet<EventLogEntry> EventLog => Set<EventLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddEventLog(Database, keepFor: KeepFor, configure: log => log.Property<string?>(Yard).HasMaxLength(64));
    }

    /// <summary>The logged apiary's model on Npgsql, for a script to be written from: it never connects.</summary>
    public static new LoggedApiaryContext ForScripts()
        => new(new DbContextOptionsBuilder<LoggedApiaryContext>().UseNpgsql("Host=nowhere.invalid;Database=unused").Options);
}

/// <summary>The apiary keeping its log for a year instead of a month: the same table, another period.</summary>
public sealed class YearLoggedApiaryContext(DbContextOptions<YearLoggedApiaryContext> options) : ApiaryContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddEventLog(Database, keepFor: TimeSpan.FromDays(365), configure: log => log.Property<string?>(LoggedApiaryContext.Yard).HasMaxLength(64));
    }
}

/// <summary>A log kept for good, under a name and in a schema of its own, next to an outbox.</summary>
public sealed class AnnalsContext(DbContextOptions<AnnalsContext> options) : DbContext(options)
{
    public DbSet<EventLogEntry> Annals => Set<EventLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.AddDomainEventOutbox(Database).AddEventLog(Database, tableName: "Annals", schema: "archive");
}

/// <summary>
/// A database of the calling test's own with the apiary on it, set up as an application whose login role holds
/// nothing sets itself up. The superuser, which stands for the role that runs the migrations, makes the tables
/// and owns them, and runs one access script written with the privileges on. The application logs in as
/// <see cref="LoginRole"/>, which owns nothing and may only switch to the roles a caller runs as and to the
/// bookkeeping role.
/// </summary>
public sealed class ApiaryDatabase
{
    /// <summary>The role the application logs in as: it owns nothing, holds nothing and inherits nothing.</summary>
    public const string LoginRole = "apiary_app";

    /// <summary>The role the toolkit's own bookkeeping runs as.</summary>
    public const string SystemRole = "ddd_system";

    public static readonly Guid AliceId = Guid.Parse("a11ce000-0000-4000-8000-000000000021");

    public static readonly Guid BobId = Guid.Parse("b0b00000-0000-4000-8000-000000000022");

    /// <summary>A keeper.</summary>
    public static readonly Caller Alice = Callers.FromClaims($$"""{"sub":"{{AliceId}}","role":"authenticated"}""");

    /// <summary>Another keeper.</summary>
    public static readonly Caller Bob = Callers.FromClaims($$"""{"sub":"{{BobId}}","role":"authenticated"}""");

    /// <summary>A ranger: a signed-in user whose token carries the role the apiary maps to a role of its own.</summary>
    public static readonly Caller Ranger = Callers.FromClaims($$"""{"sub":"0a4de400-0000-4000-8000-000000000023","role":"{{ApiaryRules.Ranger}}"}""");

    /// <summary>The server's roles are made once per server, by whichever test comes first.</summary>
    private static readonly ConcurrentDictionary<string, Task> ServerRoles = new(StringComparer.Ordinal);

    private ApiaryDatabase(string ownerConnectionString, bool logged, RowAccessExport export)
    {
        OwnerConnectionString = ownerConnectionString;
        Logged = logged;
        Export = export;
        ApplicationConnectionString = new NpgsqlConnectionStringBuilder(ownerConnectionString) { Username = LoginRole, Password = LoginRole }.ConnectionString;
    }

    /// <summary>The superuser's connection string: the tables' owner.</summary>
    public string OwnerConnectionString { get; }

    /// <summary>The application's connection string, as the role that holds nothing.</summary>
    public string ApplicationConnectionString { get; }

    /// <summary>Whether the apiary keeps an event log.</summary>
    public bool Logged { get; }

    /// <summary>What the access script was written with.</summary>
    public RowAccessExport Export { get; }

    /// <summary>The roles of the apiary's host: the defaults, the rangers' role, and the bookkeeping role for the system caller.</summary>
    public static PostgresRowLevelSecurityOptions Roles(bool system = true)
    {
        var roles = new PostgresRowLevelSecurityOptions { SystemRole = system ? SystemRole : null };
        roles.TokenRoles[ApiaryRules.Ranger] = ApiaryRules.RangerRole;
        return roles;
    }

    /// <summary>
    /// Makes the apiary in a new database of <paramref name="postgres"/>: the roles and the caller functions, the
    /// tables from the model, owned by the superuser, and one access script.
    /// </summary>
    /// <param name="postgres">The run's Postgres.</param>
    /// <param name="logged">Whether the apiary keeps an event log.</param>
    /// <param name="configure">Changes what the access script is written with; privileges on and the bookkeeping role set when left out.</param>
    /// <param name="rules">The rules; the apiary's own when left out.</param>
    /// <param name="functions">The access functions the rules ask; none when left out.</param>
    public static async Task<ApiaryDatabase> CreateAsync(
        ExplicitCallersPostgres postgres,
        bool logged = false,
        Func<RowAccessExport, RowAccessExport>? configure = null,
        IReadOnlyList<RowAccessRule>? rules = null,
        IReadOnlyList<RowAccessFunction>? functions = null)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var owner = await postgres.CreateDatabaseAsync(cancellation);
        await ServerRoles.GetOrAdd(new NpgsqlConnectionStringBuilder(owner) { Database = null }.ConnectionString, _ => MakeServerRolesAsync(owner));

        var export = new RowAccessExport { Roles = RowAccessRoleNames.From(Roles()), WriteGrants = true };
        var database = new ApiaryDatabase(owner, logged, configure?.Invoke(export) ?? export);

        await database.RunAsOwnerAsync(PostgresRowAccess.SetupScript(Roles(), LoginRole), cancellation);
        await using var model = database.ModelContext();
        await database.RunAsOwnerAsync(model.Database.GenerateCreateScript(), cancellation);
        await database.RunAsOwnerAsync(PostgresRowAccess.Script(model, rules ?? ApiaryRules.All, functions ?? [], database.Export), cancellation);

        return database;
    }

    /// <summary>
    /// The roles every apiary database shares, which are the server's: the login role, the rangers' role and the
    /// bookkeeping role, and the login role's right to switch to each role a caller runs as. Made by one
    /// connection, so two tests never grant a role at the same moment.
    /// </summary>
    private static async Task MakeServerRolesAsync(string owner)
    {
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(owner) { Pooling = false }.ConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand(
            $"""
            CREATE ROLE {LoginRole} LOGIN NOINHERIT PASSWORD '{LoginRole}';
            CREATE ROLE {ApiaryRules.RangerRole} NOLOGIN NOINHERIT;
            CREATE ROLE {SystemRole} NOLOGIN NOINHERIT;
            GRANT anon, authenticated, {ExplicitCallersPostgres.SystemInRole}, {ApiaryRules.RangerRole}, {SystemRole} TO {LoginRole};
            """,
            connection);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    /// <summary>
    /// The apiary's model on this database, as the owner, as a design-time factory makes it: with what the toolkit gives a
    /// context without services, its migration history in the apiary's schema, and nothing else of the toolkit's. For
    /// scripts, and for what the assertions read.
    /// </summary>
    public ApiaryContext ModelContext()
    {
        var options = new DbContextOptionsBuilder<ApiaryContext>().UseNpgsql(OwnerConnectionString).UseDDDToolkitDesignTime().Options;
        return Logged ? new LoggedApiaryContext(options) : new ApiaryContext(options);
    }

    /// <summary>
    /// The apiary's host, in miniature: the context on the application's connection with the toolkit's
    /// interceptors and row level security, an outbox for the hives' events, which keeps them in the event log
    /// where there is one, and explicit callers required. The context is asked for as <see cref="ApiaryContext"/>.
    /// </summary>
    /// <param name="services">Anything else about the host.</param>
    /// <param name="configure">Anything else about the toolkit's options.</param>
    /// <param name="connectionString">Another connection string than the application's.</param>
    /// <param name="dataSource">A data source to take connections from, instead of a connection string.</param>
    public ServiceProvider BuildHost(
        Action<IServiceCollection>? services = null,
        Action<DDDEntityFrameworkOptions>? configure = null,
        string? connectionString = null,
        NpgsqlDataSource? dataSource = null)
    {
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddSingleton<EventRecorder>();
        collection.AddDDDToolkitEntityFramework(options =>
        {
            options.DispatchInProcess((provider, events, cancellation) => provider.GetRequiredService<EventRecorder>().DispatchAsync(provider, events, cancellation));
            options.UseOutbox<ApiaryContext>(outbox =>
            {
                outbox.RegisterEvent<HiveSettled>().RegisterEvent<HiveRenamed>();
                if (Logged)
                {
                    outbox.KeepEventLog();
                }
            });
            configure?.Invoke(options);
        });

        collection.AddPostgresRowLevelSecurity(roles =>
        {
            roles.SystemRole = Export.Roles.System;
            roles.TokenRoles[ApiaryRules.Ranger] = ApiaryRules.RangerRole;
        });
        collection.RequireExplicitCallers();

        void Configure(IServiceProvider provider, DbContextOptionsBuilder options)
            => (dataSource is null ? options.UseNpgsql(connectionString ?? ApplicationConnectionString) : options.UseNpgsql(dataSource))
                .UseDDDToolkit(provider)
                .UsePostgresRowLevelSecurity(provider);

        if (Logged)
        {
            collection.AddDbContext<ApiaryContext, LoggedApiaryContext>(Configure);
        }
        else
        {
            collection.AddDbContext<ApiaryContext>(Configure);
        }

        collection.AddOutboxProcessor<ApiaryContext>();
        services?.Invoke(collection);

        return collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>Runs <paramref name="work"/> on the context of a new scope of <paramref name="host"/>, as <paramref name="caller"/>.</summary>
    public static async Task<T> AsAsync<T>(IServiceProvider host, Caller caller, Func<ApiaryContext, Task<T>> work)
    {
        await using var scope = host.CreateAsyncScope();
        using var running = Callers.Begin(caller);
        return await work(scope.ServiceProvider.GetRequiredService<ApiaryContext>());
    }

    /// <summary>Runs <paramref name="work"/> on the context of a new scope of <paramref name="host"/>, as <paramref name="caller"/>.</summary>
    public static Task AsAsync(IServiceProvider host, Caller caller, Func<ApiaryContext, Task> work)
        => AsAsync<object?>(host, caller, async context =>
        {
            await work(context);
            return null;
        });

    /// <summary>Runs <paramref name="sql"/> as the superuser.</summary>
    public async Task RunAsOwnerAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(OwnerConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>The first column of every row <paramref name="sql"/> answers, as text, asked as the superuser.</summary>
    public async Task<List<string>> ListAsOwnerAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(OwnerConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var rows = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture) ?? "");
        }

        return rows;
    }

    /// <summary>
    /// Every privilege a role other than the owner holds on the tables of the apiary's schemas, as the catalog has
    /// them: <c>apiary.Hives authenticated SELECT</c>, and <c>apiary.Hives authenticated UPDATE(Label)</c> for one
    /// on a column. In order.
    /// </summary>
    public Task<List<string>> PrivilegesAsync(CancellationToken cancellationToken)
        => ListAsOwnerAsync(
            $"""
            SELECT n.nspname || '.' || c.relname || ' ' || CASE WHEN acl.grantee = 0 THEN 'PUBLIC' ELSE pg_catalog.pg_get_userbyid(acl.grantee) END || ' ' || acl.privilege_type
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            CROSS JOIN LATERAL pg_catalog.aclexplode(c.relacl) acl
            WHERE n.nspname IN ('{ApiaryContext.Schema}', 'ddd') AND c.relkind = 'r' AND acl.grantee <> c.relowner
            UNION ALL
            SELECT n.nspname || '.' || c.relname || ' ' || CASE WHEN acl.grantee = 0 THEN 'PUBLIC' ELSE pg_catalog.pg_get_userbyid(acl.grantee) END || ' ' || acl.privilege_type || '(' || a.attname || ')'
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_catalog.pg_attribute a ON a.attrelid = c.oid AND NOT a.attisdropped
            CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
            WHERE n.nspname IN ('{ApiaryContext.Schema}', 'ddd') AND c.relkind = 'r'
            ORDER BY 1
            """,
            cancellationToken);
}

/// <summary>
/// Fills the yard a hive stands in on the apiary's log, from what the flow of work said with <see cref="Begin"/>:
/// a singleton, as every <see cref="IEventLogFields"/> is, that reads what it fills in each time it is asked.
/// </summary>
public sealed class YardOfTheFlow : IEventLogFields
{
    private static readonly AsyncLocal<string?> Current = new();

    /// <summary>How many events it was asked about.</summary>
    public int Asked { get; private set; }

    /// <summary>Makes <paramref name="yard"/> the yard of everything that runs until the result is disposed.</summary>
    public static IDisposable Begin(string yard)
    {
        var previous = Current.Value;
        Current.Value = yard;
        return new Ended(previous);
    }

    public void Fill(DDDToolkit.Interfaces.IDomainEvent domainEvent, Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<EventLogEntry> entry)
    {
        Asked++;

        // Asked for every log of the application; only the apiary's has the column.
        if (entry.Metadata.FindProperty(LoggedApiaryContext.Yard) is not null)
        {
            entry.Property(LoggedApiaryContext.Yard).CurrentValue = Current.Value;
        }
    }

    private sealed class Ended(string? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
