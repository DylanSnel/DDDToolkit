using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Supabase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DDDToolkit.Examples.Hosting;

/// <summary>
/// The database a host gives a module. The module decides what it stores; the host decides where.
/// </summary>
/// <remarks>
/// Four answers, each with its own idea of who creates the schema:
/// <list type="bullet">
/// <item><see cref="Sqlite"/>: a file per module, created from the model on start-up. No setup at all.</item>
/// <item><see cref="Supabase"/>: Postgres, where Supabase applies the migrations from
/// <c>supabase/migrations</c> and the application only checks that none is missing. With
/// <see cref="WithRowLevelSecurity"/>, Supabase's policies apply to the modules' queries as well.</item>
/// <item><see cref="Postgres"/>: Postgres, where the application applies its own migrations on start-up.</item>
/// <item><see cref="SqlServer"/>: SQL Server, likewise, from the module's
/// <c>DDDToolkit.Examples.{Module}.Migrations.SqlServer</c> assembly.</item>
/// </list>
/// Every module lives in a schema of its own with its own migration history, outbox and inbox, so any
/// number of modules can share one database without seeing each other's tables.
/// </remarks>
public abstract record ModuleDatabase
{
    private ModuleDatabase()
    {
    }

    /// <summary>A SQLite file per module in <paramref name="directory"/>, next to the binary when left out.</summary>
    public static ModuleDatabase Sqlite(string? directory = null) => new SqliteDatabase(directory ?? AppContext.BaseDirectory);

    /// <summary>Postgres on Supabase: the migrations are Supabase's to apply, the application checks.</summary>
    public static ModuleDatabase Supabase(string connectionString) => new PostgresDatabase(connectionString, AppliesMigrations: false);

    /// <summary>Postgres, where the application applies the migrations itself on start-up.</summary>
    public static ModuleDatabase Postgres(string connectionString) => new PostgresDatabase(connectionString, AppliesMigrations: true);

    /// <summary>SQL Server, where the application applies the migrations itself on start-up.</summary>
    public static ModuleDatabase SqlServer(string connectionString) => new SqlServerDatabase(connectionString);

    /// <summary>
    /// This Supabase database, with row level security applied to every module's own queries, not only
    /// to the Data API's: each request's queries run as the user whose Supabase access token it carried,
    /// or as <c>anon</c> without one, and work outside a request as the role the host logged in as. The
    /// host registers the interceptor with <c>services.AddSupabaseRowLevelSecurity()</c>, and who is
    /// calling with <c>AddSupabaseJwtBearer</c> from <c>DDDToolkit.Auth.Supabase.AspNetCore</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">This is not a Supabase database.</exception>
    public ModuleDatabase WithRowLevelSecurity() => this is PostgresDatabase { AppliesMigrations: false } supabase
        ? supabase with { RowLevelSecurity = true }
        : throw new InvalidOperationException("Row level security for the modules' queries is Supabase's: its roles, its auth.uid(), its policies. Call it on ModuleDatabase.Supabase(...).");

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
    /// Postgres with the migration history in <paramref name="schema"/>. The module's design-time factory
    /// calls this too, so the application and <c>dotnet ef</c> agree on where the history is.
    /// </summary>
    public static void UsePostgres(DbContextOptionsBuilder options, string connectionString, string schema)
        => options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, schema));

    /// <summary>
    /// SQL Server with the migration history in <paramref name="schema"/>, and the migrations from the
    /// assembly named after the context's own with <c>.Migrations.SqlServer</c> behind it: the module's
    /// SQL Server migrations, which a host on SQL Server references next to the module.
    /// </summary>
    public static void UseSqlServer(DbContextOptionsBuilder options, string connectionString, string schema)
    {
        ArgumentNullException.ThrowIfNull(options);

        var migrations = options.Options.ContextType.Assembly.GetName().Name + ".Migrations.SqlServer";
        options.UseSqlServer(connectionString, sql => sql
            .MigrationsHistoryTable(HistoryRepository.DefaultTableName, schema)
            .MigrationsAssembly(migrations));
    }

    /// <summary>
    /// Registers <typeparamref name="TContext"/> on this database, and whatever has to happen before the
    /// module's first query: creating the file, applying the migrations, or registering the check that
    /// Supabase applied them.
    /// </summary>
    /// <typeparam name="TContext">The module's context.</typeparam>
    /// <typeparam name="TSupabaseFactory">The module's <c>[SupabaseMigrations]</c> design-time factory.</typeparam>
    /// <param name="services">The host's services.</param>
    /// <param name="schema">The module's schema, which also names its SQLite file.</param>
    public IServiceCollection AddContext<TContext, TSupabaseFactory>(IServiceCollection services, string schema)
        where TContext : DbContext
        where TSupabaseFactory : IDesignTimeDbContextFactory<TContext>, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        services.AddDbContext<TContext>((provider, options) =>
        {
            Configure(options, schema);
            options.UseDDDToolkit(provider);

            // Every connection this context opens runs as the caller, so Supabase's policies apply to it.
            if (this is PostgresDatabase { RowLevelSecurity: true })
            {
                options.UseSupabaseRowLevelSecurity(provider);
            }
        });

        switch (this)
        {
            case SqliteDatabase:
                services.AddHostedService<PrepareDatabase<TContext>>(provider => new(provider.GetRequiredService<IServiceScopeFactory>(), migrate: false));
                break;
            case PostgresDatabase { AppliesMigrations: false }:
                services.AddSupabaseMigrations<TContext, TSupabaseFactory>();
                break;
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
            => options.UseSqlite($"Data Source={Path.Combine(Directory, schema + ".db")}")
                .ConfigureWarnings(warnings => warnings.Ignore(SqliteEventId.SchemaConfiguredWarning));
    }

    private sealed record PostgresDatabase(string ConnectionString, bool AppliesMigrations, bool RowLevelSecurity = false) : ModuleDatabase
    {
        private protected override void Configure(DbContextOptionsBuilder options, string schema)
            => UsePostgres(options, ConnectionString, schema);
    }

    private sealed record SqlServerDatabase(string ConnectionString) : ModuleDatabase
    {
        private protected override void Configure(DbContextOptionsBuilder options, string schema)
            => UseSqlServer(options, ConnectionString, schema);
    }

    /// <summary>
    /// Creates or migrates the module's database before anything else starts, the outbox poller and the
    /// seeding included: <c>StartingAsync</c> runs for every lifecycle service before any hosted
    /// service's <c>StartAsync</c>.
    /// </summary>
    private sealed class PrepareDatabase<TContext>(IServiceScopeFactory scopes, bool migrate) : IHostedLifecycleService
        where TContext : DbContext
    {
        public async Task StartingAsync(CancellationToken cancellationToken)
        {
            await using var scope = scopes.CreateAsyncScope();
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
