using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Examples.Hosting;

/// <summary>
/// The database a host gives a module. The module decides what it stores; the host decides where.
/// </summary>
/// <remarks>
/// Four answers, each with its own idea of who creates the schema:
/// <list type="bullet">
/// <item><see cref="Sqlite"/>: a file per module, created from the model on start-up. No setup at all.</item>
/// <item><see cref="Supabase"/>: Postgres, where Supabase applies the migrations from
/// <c>supabase/migrations</c> and the application only checks that none is missing, with the one call the host makes for
/// every context marked <c>[SupabaseMigrations]</c>, <c>AddSupabaseMigrations()</c>. Once the host registers
/// <c>services.AddSupabaseRowLevelSecurity()</c>, Supabase's policies apply to the modules' queries as well:
/// <c>UseDDDToolkit</c> brings it to every module's context.</item>
/// <item><see cref="Postgres"/>: Postgres, where the application applies its own migrations on start-up.</item>
/// <item><see cref="SqlServer"/>: SQL Server, likewise, from the module's
/// <c>Examples.Webshop.{Module}.Migrations.SqlServer</c> assembly.</item>
/// </list>
/// Every module lives in a schema of its own with its own migration history, outbox and inbox, so any
/// number of modules can share one database without seeing each other's tables. The history is the toolkit's to
/// place: <c>UseDDDToolkit</c> keeps it in the context's default schema, so nothing here names it.
/// </remarks>
public abstract record ModuleDatabase
{
    private ModuleDatabase()
    {
    }

    /// <summary>A SQLite file per module in <paramref name="directory"/>, next to the binary when left out.</summary>
    public static ModuleDatabase Sqlite(string? directory = null) => new SqliteDatabase(directory ?? AppContext.BaseDirectory);

    /// <summary>Postgres on Supabase: the migrations are Supabase's to apply, the host checks them with <c>AddSupabaseMigrations()</c>.</summary>
    public static ModuleDatabase Supabase(string connectionString) => new PostgresDatabase(connectionString, AppliesMigrations: false);

    /// <summary>Postgres, where the application applies the migrations itself on start-up.</summary>
    public static ModuleDatabase Postgres(string connectionString) => new PostgresDatabase(connectionString, AppliesMigrations: true);

    /// <summary>SQL Server, where the application applies the migrations itself on start-up.</summary>
    public static ModuleDatabase SqlServer(string connectionString) => new SqlServerDatabase(connectionString);

    /// <summary>
    /// The usual host's choice: Supabase when the configuration has a <c>Supabase</c> connection string,
    /// SQL Server when it has a <c>SqlServer</c> one, Postgres for a <c>Postgres</c> one, SQLite otherwise.
    /// </summary>
    public static ModuleDatabase FromConnectionStrings(Func<string, string?> connectionString)
    {
        ArgumentNullException.ThrowIfNull(connectionString);

        return connectionString("Supabase") is { Length: > 0 } supabase ? Supabase(supabase)
            : connectionString("SqlServer") is { Length: > 0 } sqlServer ? SqlServer(sqlServer)
            : connectionString("Postgres") is { Length: > 0 } postgres ? Postgres(postgres)
            : Sqlite();
    }

    /// <summary>
    /// The connection string every SQLite database of the samples is opened with: the file at
    /// <paramref name="path"/>, and no connection pool.
    /// </summary>
    /// <remarks>
    /// The driver's pool is off on purpose. Every context opens a connection of its own, so requests that
    /// arrive together open several at the same moment; and Microsoft.Data.Sqlite's pool can then hand one
    /// native connection to two of them. Two requests on one native connection is a request that fails with
    /// "unable to delete/modify user-function due to active statements", and worse, statements of one request
    /// inside the transaction of another. Without the pool every open is a native connection of its own, which
    /// costs an open and never a shared one. Postgres and SQL Server pool on the server's side of the driver
    /// and are not affected.
    /// <para>
    /// Built rather than formatted, so a path with a semicolon in it stays one path.
    /// </para>
    /// </remarks>
    /// <param name="path">The database file.</param>
    public static string SqliteConnectionString(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
    }

    /// <summary>
    /// SQL Server, with the migrations from the assembly named after the context's own with
    /// <c>.Migrations.SqlServer</c> behind it: the module's SQL Server migrations, which a host on SQL Server
    /// references next to the module. Their history is in the module's schema, where <c>UseDDDToolkit</c> keeps it,
    /// and where <c>UseDDDToolkitDesignTime()</c> keeps it for the design-time factory that calls this.
    /// </summary>
    public static DbContextOptionsBuilder UseSqlServer(DbContextOptionsBuilder options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        var migrations = options.Options.ContextType.Assembly.GetName().Name + ".Migrations.SqlServer";
        return options.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(migrations));
    }

    /// <summary>
    /// Registers <typeparamref name="TContext"/> on this database, and whatever has to happen before the
    /// module's first query: creating the file or applying the migrations. On Supabase nothing does: the migrations
    /// are Supabase's to apply, and the host checks them for every module at once, with <c>AddSupabaseMigrations()</c>.
    /// </summary>
    /// <typeparam name="TContext">The module's context.</typeparam>
    /// <param name="services">The host's services.</param>
    /// <param name="schema">
    /// The module's schema, which names its SQLite file. On the other databases the model says it, and the migration
    /// history goes there by itself.
    /// </param>
    /// <param name="configure">
    /// What the module adds to its context's options on every database. It runs after <c>UseDDDToolkit</c>, so an
    /// interceptor it adds sees a save after the toolkit's interceptors, and the parts the host's registrations
    /// brought, have.
    /// </param>
    public IServiceCollection AddContext<TContext>(
        IServiceCollection services,
        string schema,
        Action<IServiceProvider, DbContextOptionsBuilder>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        void Options(IServiceProvider provider, DbContextOptionsBuilder options)
        {
            // The provider first, so UseDDDToolkit can tell which of what the host registered belongs here: on
            // Supabase with row level security registered, every connection this context opens runs as the caller,
            // so Supabase's policies apply to it; on SQLite there is nothing of the kind.
            Configure(options, schema);
            options.UseDDDToolkit(provider);
            configure?.Invoke(provider, options);
        }

        // A context per scope. A module whose reads each take a context of their own registers on the host's
        // pools instead (PostgresPools.AddContext).
        services.AddDbContext<TContext>(Options);

        switch (this)
        {
            case SqliteDatabase:
                services.AddHostedService<PrepareDatabase<TContext>>(provider => new(provider.GetRequiredService<IServiceScopeFactory>(), migrate: false));
                break;
            // Supabase: the CLI applies the migrations, and the host checks them all with AddSupabaseMigrations().
            case PostgresDatabase { AppliesMigrations: false }:
                break;
            // Postgres and SQL Server: the application applies the module's migrations.
            default:
                services.AddHostedService<PrepareDatabase<TContext>>(provider => new(provider.GetRequiredService<IServiceScopeFactory>(), migrate: true));
                break;
        }

        return services;
    }

    private protected abstract void Configure(DbContextOptionsBuilder options, string schema);

    private sealed record SqliteDatabase(string Directory) : ModuleDatabase
    {
        private protected override void Configure(DbContextOptionsBuilder options, string schema)
            // SQLite has no schemas, so the module's schema is dropped. Saying so on every start teaches nothing.
            => options.UseSqlite(SqliteConnectionString(Path.Combine(Directory, schema + ".db")))
                .ConfigureWarnings(warnings => warnings.Ignore(SqliteEventId.SchemaConfiguredWarning));
    }

    private sealed record PostgresDatabase(string ConnectionString, bool AppliesMigrations) : ModuleDatabase
    {
        // The migrations are the ones next to the context, in its own assembly, where Entity Framework looks when it
        // is told nothing else; their history is in the module's schema, where UseDDDToolkit keeps it.
        private protected override void Configure(DbContextOptionsBuilder options, string schema)
            => options.UseNpgsql(ConnectionString);
    }

    private sealed record SqlServerDatabase(string ConnectionString) : ModuleDatabase
    {
        private protected override void Configure(DbContextOptionsBuilder options, string schema)
            => UseSqlServer(options, ConnectionString);
    }

    /// <summary>
    /// Creates or migrates the module's database before anything else starts, the outbox poller and the
    /// seeding included: <c>StartingAsync</c> runs for every lifecycle service before any hosted
    /// service's <c>StartAsync</c>. It is the application's own work, so it says so, which a host that
    /// requires explicit callers needs.
    /// </summary>
    private sealed class PrepareDatabase<TContext>(IServiceScopeFactory scopes, bool migrate) : IHostedLifecycleService
        where TContext : DbContext
    {
        public async Task StartingAsync(CancellationToken cancellationToken)
        {
            await using var scope = scopes.CreateAsyncScope();
            using var system = Callers.Begin(Caller.System);
            var database = scope.ServiceProvider.GetRequiredService<TContext>().Database;

            if (migrate)
            {
                await database.MigrateAsync(cancellationToken);
            }
            else
            {
                await database.EnsureCreatedAsync(cancellationToken);
            }
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
