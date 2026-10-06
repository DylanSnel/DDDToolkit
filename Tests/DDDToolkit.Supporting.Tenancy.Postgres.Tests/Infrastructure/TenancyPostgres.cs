using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.TestHost;
using DDDToolkit.Supporting.Tenancy.TestHost.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancyPostgres))]

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure;

/// <summary>
/// One Postgres for every test of this run that needs one, started the first time a test asks for a database, so
/// the tests that need none start no container, and stopped with the run. Each test gets a database of its own,
/// copied from one of two templates, prepared once for each <see cref="TenancyNaming"/> a test asks for:
/// <list type="bullet">
/// <item><see cref="Template.Plain"/>: Tenancy's tables and the widgets', as the application's migrations make
/// them, with the privileges callers need and no row level security.</item>
/// <item><see cref="Template.Secured"/>: that, and the access files, Tenancy's contribution and the widgets' rules,
/// with the data of <see cref="TenancySeed"/> seeded through the use cases, under the policies.</item>
/// </list>
/// The templates of another naming are made by the same steps from the same classes: only the context's options
/// differ, and with them every name in the tables' script and in the access files.
/// </summary>
/// <remarks>
/// The application logs in as <see cref="LoginRole"/>, which owns the schemas and the tables, as a host that
/// migrates its own database does; it may switch to the callers' roles and to the scoped system role, which the
/// setup script makes, but inherits none of their privileges. The superuser only makes the login role, the
/// databases and the schemas. Every connection that fills or copies a template is unpooled: a pooled one would
/// stay connected after it closed, and a database cannot be copied while anybody is connected to it.
/// </remarks>
public sealed class TenancyPostgres : IAsyncLifetime
{
    /// <summary>Supabase's Postgres 17, with the pgmq it ships.</summary>
    public const string Image = "ghcr.io/pgmq/pg17-pgmq:v1.5.1";

    /// <summary>The role the application logs in as: it owns the tables, and switches to the callers' roles.</summary>
    public const string LoginRole = "tenancy_app";

    private const string LoginPassword = "tenancy_app";

    /// <summary>A template cannot be copied twice at once, so every copy waits its turn.</summary>
    private static readonly SemaphoreSlim Copying = new(1, 1);

    private readonly SemaphoreSlim _starting = new(1, 1);
    private readonly SemaphoreSlim _preparing = new(1, 1);
    private readonly Dictionary<(TenancyNaming Names, Template Template), Task> _templates = [];
    private PostgreSqlContainer? _container;
    private Task<bool>? _start;
    private string? _whyNot;
    private bool _available;
    private int _databases;

    /// <summary>The templates a test's database is copied from.</summary>
    public enum Template
    {
        /// <summary>The tables and the privileges, without row level security.</summary>
        Plain,

        /// <summary>The tables, the access files, and the seeded data.</summary>
        Secured,
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// A new database of the calling test's own, copied from <paramref name="template"/> as it is under
    /// <paramref name="names"/>, the default names unless another naming is given. Skips the test without Docker,
    /// or fails it where containers are required, with what the start said.
    /// </summary>
    public async Task<TestDatabase> CreateDatabaseAsync(Template template, CancellationToken cancellationToken, TenancyNaming? names = null)
    {
        names ??= TenancyNaming.Default;
        await StartAsync(cancellationToken);
        RequiredContainers.EnforceOrSkip(
            _available,
            RequiredContainers.Required,
            "PostgreSQL",
            $"'{Image}' could not be started, as on a machine without Docker ({_whyNot}). Tenancy's policies, functions and triggers are not covered on this machine.");

        await PreparedAsync(names, template, cancellationToken);

        var name = $"tenancy_{Interlocked.Increment(ref _databases)}";
        await Copying.WaitAsync(cancellationToken);
        try
        {
            await ExecuteAsync(Superuser(), $"CREATE DATABASE {name} TEMPLATE {TemplateOf(names, template)} OWNER {LoginRole}", cancellationToken);
        }
        finally
        {
            Copying.Release();
        }

        return new TestDatabase(Login(name), Superuser(name), names);
    }

    /// <summary>The catalogue the TestHost runs with, as the export builds it for Tenancy's contribution.</summary>
    public static TenancyCatalogue Catalogue { get; } = TenancyCatalogue.Build(HostCatalogue.Application, []);

    /// <summary>
    /// The access files of the TestHost and the widgets, in the order to run them, written with Tenancy's
    /// contribution built from <paramref name="catalogue"/>, or the TestHost's own, the widgets'
    /// <paramref name="rules"/>, or their own, the access <paramref name="functions"/> a test adds, and the
    /// <paramref name="roles"/> of the host, or the defaults, from the models the contexts have under
    /// <paramref name="names"/>, the default names unless another naming is given. With <paramref name="operators"/>
    /// the contribution is told which token roles are operators'; <paramref name="more"/> are further contexts of
    /// modules the host has, written after the widgets'; and <paramref name="export"/> turns on what an export
    /// leaves off by default, the privileges or the forced policies.
    /// </summary>
    public static IReadOnlyList<string> AccessScripts(
        TenancyCatalogue? catalogue = null,
        IReadOnlyList<RowAccessRule>? rules = null,
        IReadOnlyList<RowAccessFunction>? functions = null,
        TenancyNaming? names = null,
        RowAccessRoleNames? roles = null,
        IReadOnlyCollection<string>? operators = null,
        IReadOnlyList<DbContext>? more = null,
        Func<RowAccessExport, RowAccessExport>? export = null)
    {
        names ??= TenancyNaming.Default;
        using var tenancy = names.TenancyModel();
        using var widgets = names.WidgetModel();
        var written = new RowAccessExport
        {
            Roles = roles ?? RowAccessRoleNames.Default,
            Contributions = [new TenancyRowAccessContribution(catalogue ?? Catalogue, operators)],
        };

        return [.. PostgresRowAccess.Scripts([tenancy, widgets, .. more ?? []], rules ?? WidgetRules.All, functions ?? [], export?.Invoke(written) ?? written)
            .Select(each => each.Script)];
    }

    /// <summary>The role that owns the tables and the functions of a database <see cref="ForceAsync"/> forced: it bypasses the policies.</summary>
    public const string MigrationRole = "tenancy_forced_migrations";

    /// <summary>
    /// Writes the access files of <paramref name="database"/> again with every policy forced on the tables' owner,
    /// from <paramref name="rules"/> or the widgets' own, and hands the tables, the functions and the schemas to
    /// <see cref="MigrationRole"/>, which may bypass the policies, as the role that runs an application's migrations
    /// does: under forced policies the functions that run as their owner see nothing otherwise. The application keeps
    /// logging in as the role that owned them, which now owns nothing and becomes its callers' roles.
    /// </summary>
    public static async Task ForceAsync(TestDatabase database, CancellationToken cancellationToken, IReadOnlyList<RowAccessRule>? rules = null)
    {
        foreach (var script in AccessScripts(rules: rules, names: database.Names, export: written => new RowAccessExport { Roles = written.Roles, Contributions = written.Contributions, ForceRowLevelSecurity = true }))
        {
            await ExecuteAsync(database.ConnectionString, script, cancellationToken);
        }

        // The role is the server's, and a test of another class may be making it at the same time: the second finds it.
        await ExecuteAsync(database.SuperuserConnectionString, $"""
            DO $roles$
            DECLARE
                owned record;
            BEGIN
                BEGIN
                    CREATE ROLE {MigrationRole} NOLOGIN BYPASSRLS;
                EXCEPTION WHEN duplicate_object OR unique_violation THEN
                    NULL;
                END;

                FOR owned IN
                    SELECT pg_catalog.format('ALTER TABLE %I.%I OWNER TO {MigrationRole}', n.nspname, c.relname) AS statement
                    FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname IN ('ddd', 'tenancy', 'widgets') AND c.relkind = 'r'
                    UNION ALL
                    SELECT pg_catalog.format('ALTER ROUTINE %s OWNER TO {MigrationRole}', p.oid::pg_catalog.regprocedure)
                    FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                    WHERE n.nspname IN ('ddd', 'tenancy', 'widgets')
                    UNION ALL
                    SELECT pg_catalog.format('ALTER SCHEMA %I OWNER TO {MigrationRole}', n.nspname)
                    FROM pg_catalog.pg_namespace n
                    WHERE n.nspname IN ('ddd', 'tenancy', 'widgets')
                LOOP
                    EXECUTE owned.statement;
                END LOOP;
            END
            $roles$;
            """, cancellationToken);
    }

    /// <summary>Tenancy's context on Npgsql under the default names, for its model: it never connects.</summary>
    public static TestTenancyContext TenancyModel() => TenancyNaming.Default.TenancyModel();

    /// <summary>The widgets' context on Npgsql under the default names, for its model: it never connects.</summary>
    public static WidgetContext WidgetModel() => TenancyNaming.Default.WidgetModel();

    /// <summary>Runs <paramref name="sql"/> on a new, unpooled connection, closed when it is done.</summary>
    public static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(Unpooled(connectionString));
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary><paramref name="connectionString"/> without pooling.</summary>
    public static string Unpooled(string connectionString)
        => new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;

    /// <summary>The template's database: <c>tenancy_plain</c> and <c>tenancy_secured</c>, <c>tenancy_snake_plain</c> and <c>tenancy_snake_secured</c>.</summary>
    private static string TemplateOf(TenancyNaming names, Template template) => names.DatabasePrefix + (template == Template.Plain ? "_plain" : "_secured");

    private string Superuser(string? database = null)
        => new NpgsqlConnectionStringBuilder(_container!.GetConnectionString()) { Database = database ?? "postgres" }.ConnectionString;

    /// <summary>
    /// As the login role, to <paramref name="database"/>: pooled, as an application connects, with idle connections
    /// closed after seconds rather than minutes, since no test comes back to a database once it is done.
    /// </summary>
    private string Login(string database)
        => new NpgsqlConnectionStringBuilder(_container!.GetConnectionString())
        {
            Database = database,
            Username = LoginRole,
            Password = LoginPassword,
            IncludeErrorDetail = true,
            ConnectionIdleLifetime = 10,
            ConnectionPruningInterval = 5,
        }.ConnectionString;

    /// <summary>
    /// The server, started the first time a test asks: one start for the run, which no test's own token ends, so
    /// a test that is cancelled while it waits leaves the start to the others. Without Docker there is no server
    /// and every test is skipped, or failed where containers are required, with what the start said: a container
    /// that does not start on a machine that has Docker reads the same, and the reason tells them apart. A server
    /// that started and could not be set up fails every test that asks, with the reason: it is never taken for a
    /// machine without Docker.
    /// </summary>
    private async Task StartAsync(CancellationToken cancellationToken)
    {
        Task<bool> start;
        await _starting.WaitAsync(cancellationToken);
        try
        {
            start = _start ??= StartServerAsync();
        }
        finally
        {
            _starting.Release();
        }

        _available = await start.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Whether a server was started, and why not when it was not; a server that started is set up, or this fails.
    /// </summary>
    private async Task<bool> StartServerAsync()
    {
        // Every test has a database of its own, and the pools of the tests before it keep idle connections to
        // theirs open for a while: more than the default hundred.
        var container = new PostgreSqlBuilder(Image)
            .WithDatabase("postgres")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithCommand("-c", "max_connections=500")
            .Build();
        try
        {
            await container.StartAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Kept for the test that asks: a skip or a failure that says only "no Docker" hides a pull that was
            // refused, a port that was taken, or a server that did not come up in time.
            _whyNot = exception.Message.Trim();
            await container.DisposeAsync();
            return false;
        }

        _container = container;
        await ExecuteAsync(Superuser(), $"CREATE ROLE {LoginRole} LOGIN NOINHERIT PASSWORD '{LoginPassword}'", CancellationToken.None);
        return true;
    }

    /// <summary>
    /// The template under <paramref name="names"/>, prepared the first time a test asks for it: a test of the plain
    /// tables does not wait for the secured template, nor fail with it, and a run that asks for one naming alone
    /// prepares no other. A template that could not be prepared fails every test that asks for it, with the reason.
    /// </summary>
    private async Task PreparedAsync(TenancyNaming names, Template template, CancellationToken cancellationToken)
    {
        Task prepared;
        await _starting.WaitAsync(cancellationToken);
        try
        {
            if (!_templates.TryGetValue((names, Template.Plain), out var plain))
            {
                _templates[(names, Template.Plain)] = plain = PreparePlainAsync(names, CancellationToken.None);
            }

            prepared = plain;
            if (template == Template.Secured && !_templates.TryGetValue((names, Template.Secured), out prepared!))
            {
                _templates[(names, Template.Secured)] = prepared = PrepareSecuredAsync(names, plain, CancellationToken.None);
            }
        }
        finally
        {
            _starting.Release();
        }

        await prepared.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// The plain template: its schemas made by the superuser for the login role, the setup script run by the
    /// superuser, which makes the callers' roles and the scoped system role and grants them to the login role, and
    /// the tables made by the login role from the models the contexts have under <paramref name="names"/>, with what
    /// callers may do with them: everything on Tenancy's tables, the widgets' and the access history, so that only
    /// the policies and the triggers stand in a caller's way, and adding rows to the outbox.
    /// </summary>
    private async Task PreparePlainAsync(TenancyNaming names, CancellationToken cancellationToken)
    {
        // One template at a time: the setup script makes roles, which are the server's and not a database's.
        await _preparing.WaitAsync(cancellationToken);
        try
        {
            await PlainAsync(names, cancellationToken);
        }
        finally
        {
            _preparing.Release();
        }
    }

    private async Task PlainAsync(TenancyNaming names, CancellationToken cancellationToken)
    {
        var template = TemplateOf(names, Template.Plain);
        await ExecuteAsync(Superuser(), $"CREATE DATABASE {template} OWNER {LoginRole}", cancellationToken);

        var owner = Superuser(template);
        await ExecuteAsync(
            owner,
            $"CREATE SCHEMA ddd AUTHORIZATION {LoginRole}; CREATE SCHEMA {TestTenancyContext.Schema} AUTHORIZATION {LoginRole}; CREATE SCHEMA {WidgetContext.Schema} AUTHORIZATION {LoginRole};",
            cancellationToken);
        await ExecuteAsync(owner, PostgresRowAccess.SetupScript(loginRole: LoginRole), cancellationToken);

        var login = Login(template);
        await using (var tenancy = names.TenancyModel())
        await using (var widgets = names.WidgetModel())
        {
            await ExecuteAsync(login, tenancy.Database.GenerateCreateScript(), cancellationToken);
            await ExecuteAsync(login, widgets.Database.GenerateCreateScript(), cancellationToken);
        }

        await ExecuteAsync(
            login,
            names.Sql($"""
            GRANT USAGE ON SCHEMA {TestTenancyContext.Schema}, {WidgetContext.Schema} TO authenticated, anon, ddd_system_in;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {TestTenancyContext.Schema}, {WidgetContext.Schema} TO authenticated, anon, ddd_system_in;
            GRANT INSERT ON ddd."OutboxMessages" TO authenticated, ddd_system_in;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ddd."EventLog" TO authenticated, anon, ddd_system_in;
            """),
            cancellationToken);
    }

    /// <summary>The secured template: a copy of the plain one, with the access files and the seeded data.</summary>
    private async Task PrepareSecuredAsync(TenancyNaming names, Task plain, CancellationToken cancellationToken)
    {
        await plain;
        var template = TemplateOf(names, Template.Secured);
        await Copying.WaitAsync(cancellationToken);
        try
        {
            await ExecuteAsync(Superuser(), $"CREATE DATABASE {template} TEMPLATE {TemplateOf(names, Template.Plain)} OWNER {LoginRole}", cancellationToken);
        }
        finally
        {
            Copying.Release();
        }

        var login = Login(template);
        foreach (var script in AccessScripts(names: names))
        {
            await ExecuteAsync(login, script, cancellationToken);
        }

        await TenancySeed.SeedAsync(new TestDatabase(Unpooled(login), Superuser(template), names), cancellationToken);
        NpgsqlConnection.ClearAllPools();
    }
}

/// <summary>A test's own database: how the application connects to it, how the superuser does, and what it calls things.</summary>
/// <param name="ConnectionString">As the login role, which owns the tables.</param>
/// <param name="SuperuserConnectionString">As the superuser, for what only it may do, such as making roles.</param>
/// <param name="Names">The naming its tables were made under, which every context and every statement sent to it follows.</param>
public sealed record TestDatabase(string ConnectionString, string SuperuserConnectionString, TenancyNaming Names);
