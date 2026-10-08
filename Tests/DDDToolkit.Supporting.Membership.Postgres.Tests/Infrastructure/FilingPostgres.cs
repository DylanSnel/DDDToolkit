using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Membership.TestHost.Access;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(DDDToolkit.Supporting.Membership.Postgres.Tests.Infrastructure.FilingPostgres))]

namespace DDDToolkit.Supporting.Membership.Postgres.Tests.Infrastructure;

/// <summary>
/// One Postgres for every test of this run that needs one, started the first time a test asks for a database, so
/// the tests that need none start no container, and stopped with the run. Each test gets a database of its own,
/// copied from one of two templates:
/// <list type="bullet">
/// <item><see cref="Template.Plain"/>: the application's tables, as its migrations make them, with the privileges
/// callers need, and no access file applied: no function of Membership's, and no policy.</item>
/// <item><see cref="Template.Secured"/>: that, and the access file: the functions the two contributions write, and
/// the policies of the application's own rules, which ask them.</item>
/// </list>
/// </summary>
/// <remarks>
/// The application logs in as <see cref="LoginRole"/>, which owns the schema and the tables, as a host that
/// migrates its own database does; it may switch to the callers' roles, which the setup script makes, but
/// inherits none of their privileges. The superuser only makes the login role, the databases and the schemas.
/// Every connection that fills or copies a template is unpooled: a database cannot be copied while anybody is
/// connected to it.
/// </remarks>
public sealed class FilingPostgres : IAsyncLifetime
{
    /// <summary>Postgres 17, the image the toolkit's other suites start as well.</summary>
    public const string Image = "ghcr.io/pgmq/pg17-pgmq:v1.5.1";

    /// <summary>The role the application logs in as: it owns the tables, and switches to the callers' roles.</summary>
    public const string LoginRole = "filing_app";

    /// <summary>The token role the folder's rules let ask its functions, next to signed-in users.</summary>
    public const string ArchivistTokenRole = "archivist";

    /// <summary>The database role the host maps that token role to.</summary>
    public const string ArchivistRole = "filing_archivist";

    private const string LoginPassword = "filing_app";

    /// <summary>A template cannot be copied twice at once, so every copy waits its turn.</summary>
    private static readonly SemaphoreSlim Copying = new(1, 1);

    private readonly SemaphoreSlim _starting = new(1, 1);
    private readonly Dictionary<Template, Task> _templates = [];
    private readonly Dictionary<string, Task> _named = new(StringComparer.Ordinal);
    private PostgreSqlContainer? _container;
    private Task<bool>? _start;
    private bool _available;
    private string? _notStarted;
    private int _databases;

    /// <summary>The templates a test's database is copied from.</summary>
    public enum Template
    {
        /// <summary>The tables and the privileges, with no access file applied.</summary>
        Plain,

        /// <summary>The tables, and the access file: Membership's functions and the application's policies.</summary>
        Secured,
    }

    /// <summary>The application's own row access rules, as a script is written from them.</summary>
    public static IReadOnlyList<RowAccessRule> Rules { get; } =
    [
        RowAccessRule.For<Document>("Users read the documents they see", RowOperations.Read, UsersReadTheDocumentsTheySee.RowAccessSql, RowAccessRoles.User),
        RowAccessRule.For<Document>("Users change the documents they work on", RowOperations.Change, UsersChangeTheDocumentsTheyWorkOn.RowAccessSql, RowAccessRoles.User),
        RowAccessRule.For<Folder>("Staff read the folders they are on", RowOperations.Read, StaffReadTheFoldersTheyAreOn.RowAccessSql, RowAccessRoles.User),
    ];

    /// <summary>
    /// The contributions the access file is written with: the package's own class once for each kind of resource,
    /// closed over its member class, with its rules. An application lists none of them: the Supabase build makes each
    /// one in the project that runs the export, from the rules it marks <c>[MembershipRules&lt;TMember&gt;]</c>. This
    /// suite runs no export, so it makes the package's class itself, from the rules an application would mark.
    /// </summary>
    public static IReadOnlyList<IRowAccessContribution> Contributions =>
    [
        new MembershipRowAccessContribution<DocumentShare>(DocumentMembership.Rules),
        new MembershipRowAccessContribution<FolderMember>(FolderMembership.Rules),
    ];

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>The host's roles: the defaults, and the folder's token role mapped to a database role of its own.</summary>
    public static void Roles(PostgresRowLevelSecurityOptions options) => options.TokenRoles[ArchivistTokenRole] = ArchivistRole;

    /// <summary>
    /// The access file of the application's context, written with <paramref name="contributions"/>, or
    /// <see cref="Contributions"/>, and the application's <paramref name="rules"/>, or its own. With <paramref name="writeGrants"/>
    /// it writes the tables' privileges from the policies as well, and with <paramref name="force"/> it holds
    /// the tables' owner to their policies too.
    /// </summary>
    public static string AccessScript(IReadOnlyList<IRowAccessContribution>? contributions = null, IReadOnlyList<RowAccessRule>? rules = null, bool writeGrants = false, bool force = false)
    {
        using var context = Model();

        return PostgresRowAccess.Script(context, rules ?? Rules, null, new RowAccessExport
        {
            Roles = RoleNames,
            Contributions = contributions ?? Contributions,
            WriteGrants = writeGrants,
            ForceRowLevelSecurity = force,
        });
    }

    /// <summary>The database roles the host's rules are written for: the defaults, and the folder's token role as the host maps it.</summary>
    public static RowAccessRoleNames RoleNames
    {
        get
        {
            var options = new PostgresRowLevelSecurityOptions();
            Roles(options);
            return RowAccessRoleNames.From(options);
        }
    }

    /// <summary>The application's context on Npgsql, for its model: it never connects.</summary>
    public static FilingContext Model() => new(new DbContextOptionsBuilder<FilingContext>().UseNpgsql("Host=model-only").Options);

    /// <summary>A new database of the calling test's own, copied from <paramref name="template"/>. Skips the test without Docker, or fails it where containers are required.</summary>
    public async Task<FilingDatabase> CreateDatabaseAsync(Template template, CancellationToken cancellationToken)
    {
        await StartAsync(cancellationToken);
        RequiredContainers.EnforceOrSkip(
            _available,
            RequiredContainers.Required,
            "PostgreSQL",
            NotCovered());

        await PreparedAsync(template, cancellationToken);

        var name = $"filing_{Interlocked.Increment(ref _databases)}";
        await Copying.WaitAsync(cancellationToken);
        try
        {
            await ExecuteAsync(Superuser(), $"CREATE DATABASE {name} TEMPLATE {TemplateOf(template)} OWNER {LoginRole}", cancellationToken);
        }
        finally
        {
            Copying.Release();
        }

        return new FilingDatabase(Login(name), Superuser(name));
    }

    /// <summary>
    /// A new database of the calling test's own, copied from a template of another application's than the
    /// filing one: made the first time a test asks for it, as an empty database the login role owns that
    /// <paramref name="prepare"/> fills, and copied for every test after. Skips the test without Docker, or
    /// fails it where containers are required.
    /// </summary>
    /// <param name="template">The template's name: lower case letters and underscores.</param>
    /// <param name="prepare">Fills the template, once for the run: its schemas, its tables and whatever else every test of it starts from.</param>
    /// <param name="cancellationToken">Cancels the waiting; a template that is being prepared is prepared all the same.</param>
    public async Task<FilingDatabase> CreateDatabaseAsync(string template, Func<FilingDatabase, CancellationToken, Task> prepare, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        if (template.Length == 0 || !template.All(character => character is (>= 'a' and <= 'z') or '_'))
        {
            throw new ArgumentException("A template is named in lower case letters and underscores.", nameof(template));
        }

        await StartAsync(cancellationToken);
        RequiredContainers.EnforceOrSkip(
            _available,
            RequiredContainers.Required,
            "PostgreSQL",
            NotCovered());

        Task prepared;
        await _starting.WaitAsync(cancellationToken);
        try
        {
            if (!_named.TryGetValue(template, out prepared!))
            {
                _named[template] = prepared = PrepareNamedAsync(template, prepare, CancellationToken.None);
            }
        }
        finally
        {
            _starting.Release();
        }

        await prepared.WaitAsync(cancellationToken);

        var name = $"{template}_{Interlocked.Increment(ref _databases)}";
        await Copying.WaitAsync(cancellationToken);
        try
        {
            await ExecuteAsync(Superuser(), $"CREATE DATABASE {name} TEMPLATE {template} OWNER {LoginRole}", cancellationToken);
        }
        finally
        {
            Copying.Release();
        }

        return new FilingDatabase(Login(name), Superuser(name));
    }

    /// <summary>A template of the caller's own: an empty database the login role owns, filled by <paramref name="prepare"/>.</summary>
    private async Task PrepareNamedAsync(string template, Func<FilingDatabase, CancellationToken, Task> prepare, CancellationToken cancellationToken)
    {
        await ExecuteAsync(Superuser(), $"CREATE DATABASE {template} OWNER {LoginRole}", cancellationToken);
        await prepare(new FilingDatabase(Login(template), Superuser(template)), cancellationToken);
        NpgsqlConnection.ClearAllPools();
    }

    /// <summary>
    /// Why the tests that need the server are not run: that it could not be started, with what the start said,
    /// so a machine that has Docker and could not pull the image is not read as one without Docker.
    /// </summary>
    private string NotCovered()
        => $"'{Image}' could not be started, so Membership's functions and its start-up check are not covered on this machine. The start said: {_notStarted ?? "nothing"}";

    /// <summary>Runs <paramref name="sql"/> on a new, unpooled connection, closed when it is done.</summary>
    public static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string TemplateOf(Template template) => template == Template.Plain ? "filing_plain" : "filing_secured";

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
    /// The server, started the first time a test asks: one start for the run, which no test's own token ends.
    /// A server that could not be started, without Docker or for any other reason, is no server: every test is
    /// skipped, or failed where containers are required, and told what the start said. A server that started
    /// and could not be set up fails every test that asks, with the reason.
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

    private async Task<bool> StartServerAsync()
    {
        // Every test has a database of its own, and the pools of the tests before it keep idle connections to
        // theirs open for a while: more than the default hundred.
        var container = new PostgreSqlBuilder(Image)
            .WithDatabase("postgres")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithCommand("-c", "max_connections=300")
            .Build();
        try
        {
            await container.StartAsync(CancellationToken.None);
        }
        catch (Exception failed)
        {
            // Kept, and said where a test is skipped or failed: no Docker, an image that could not be pulled
            // and a start that timed out are three things, and only the text tells them apart.
            _notStarted = failed.GetType().Name + ": " + failed.Message;
            await container.DisposeAsync();
            return false;
        }

        _container = container;
        await ExecuteAsync(Superuser(), $"CREATE ROLE {LoginRole} LOGIN NOINHERIT PASSWORD '{LoginPassword}'", CancellationToken.None);
        return true;
    }

    /// <summary>The template, prepared the first time a test asks for it; one that could not be prepared fails every test that asks for it, with the reason.</summary>
    private async Task PreparedAsync(Template template, CancellationToken cancellationToken)
    {
        Task prepared;
        await _starting.WaitAsync(cancellationToken);
        try
        {
            if (!_templates.TryGetValue(Template.Plain, out var plain))
            {
                _templates[Template.Plain] = plain = PreparePlainAsync(CancellationToken.None);
            }

            prepared = plain;
            if (template == Template.Secured && !_templates.TryGetValue(Template.Secured, out prepared!))
            {
                _templates[Template.Secured] = prepared = PrepareSecuredAsync(plain, CancellationToken.None);
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
    /// superuser, which makes the callers' roles and grants them to the login role, and the tables made by the
    /// login role from the application's model, with what callers may do with them: everything, so that only
    /// the policies stand in a caller's way.
    /// </summary>
    private async Task PreparePlainAsync(CancellationToken cancellationToken)
    {
        var template = TemplateOf(Template.Plain);
        await ExecuteAsync(Superuser(), $"CREATE DATABASE {template} OWNER {LoginRole}", cancellationToken);

        var owner = Superuser(template);
        await ExecuteAsync(owner, $"CREATE SCHEMA ddd AUTHORIZATION {LoginRole}; CREATE SCHEMA {FilingContext.Schema} AUTHORIZATION {LoginRole};", cancellationToken);

        var options = new PostgresRowLevelSecurityOptions();
        Roles(options);
        await ExecuteAsync(owner, PostgresRowAccess.SetupScript(options, loginRole: LoginRole), cancellationToken);

        var login = Login(template);
        await using (var context = Model())
        {
            await ExecuteAsync(login, context.Database.GenerateCreateScript(), cancellationToken);
        }

        await ExecuteAsync(
            login,
            $"""
            GRANT USAGE ON SCHEMA {FilingContext.Schema} TO authenticated, anon, ddd_system_in, {ArchivistRole};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {FilingContext.Schema} TO authenticated, anon, ddd_system_in, {ArchivistRole};
            """,
            cancellationToken);
        NpgsqlConnection.ClearAllPools();
    }

    /// <summary>The secured template: a copy of the plain one, with the access file applied by the login role.</summary>
    private async Task PrepareSecuredAsync(Task plain, CancellationToken cancellationToken)
    {
        await plain;
        var template = TemplateOf(Template.Secured);
        await Copying.WaitAsync(cancellationToken);
        try
        {
            await ExecuteAsync(Superuser(), $"CREATE DATABASE {template} TEMPLATE {TemplateOf(Template.Plain)} OWNER {LoginRole}", cancellationToken);
        }
        finally
        {
            Copying.Release();
        }

        await ExecuteAsync(Login(template), AccessScript(), cancellationToken);
        NpgsqlConnection.ClearAllPools();
    }
}

/// <summary>A test's own database: how the application connects to it, and how the superuser does.</summary>
/// <param name="ConnectionString">As the login role, which owns the tables.</param>
/// <param name="SuperuserConnectionString">As the superuser, for what only it may do.</param>
public sealed record FilingDatabase(string ConnectionString, string SuperuserConnectionString);
