using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Membership.Postgres;
using DDDToolkit.Supporting.Tenancy.Postgres;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(DDDToolkit.Supporting.Together.Tests.Infrastructure.CampusPostgres))]
[assembly: AssemblyFixture(typeof(DDDToolkit.Supporting.Together.Tests.Infrastructure.QuotedCampusPostgres))]

namespace DDDToolkit.Supporting.Together.Tests.Infrastructure;

/// <summary>
/// One Postgres for every test of this run that asks for one, started the first time a test does, so the tests
/// on SQLite start no container; stopped with the run. Each test gets a database of its own, copied from a
/// template that holds Tenancy's tables and the courses', with what callers may do with them, and the access
/// files of both contexts: Tenancy's policies, functions and triggers, the functions of the courses and the
/// labs, which ask Tenancy's, and the application's own policies, which ask those.
/// </summary>
/// <remarks>
/// The application logs in as <see cref="LoginRole"/>, which owns the schemas and the tables, as a host that
/// migrates its own database does; it may switch to the callers' roles and to the scoped system role, which the
/// setup script makes, but inherits none of their privileges. The superuser only makes the login role, the
/// databases and the schemas. Every connection that fills or copies the template is unpooled: a database
/// cannot be copied while anybody is connected to it.
/// </remarks>
public sealed class CampusPostgres() : CampusPostgresServer(CampusSchemas.Default)
{
    /// <summary>Postgres 17, the image the toolkit's other suites start as well.</summary>
    public const string Image = "ghcr.io/pgmq/pg17-pgmq:v1.5.1";

    /// <summary>The role the application logs in as: it owns the tables, and switches to the callers' roles.</summary>
    public const string LoginRole = "campus_app";

    /// <summary>The application's own row access rules, as a script is written from them.</summary>
    public static IReadOnlyList<RowAccessRule> Rules { get; } =
    [
        RowAccessRule.For<Course>("Seats read the courses they see", RowOperations.Read, SeatsReadTheCoursesTheySee.RowAccessSql, RowAccessRoles.User),
        RowAccessRule.For<CourseRole>("Seats read the roles of courses of their college", RowOperations.Read, SeatsReadTheRolesOfCoursesOfTheirCollege.RowAccessSql, RowAccessRoles.User),
        RowAccessRule.For<CourseRole>("Role managers change the roles of courses", RowOperations.Create | RowOperations.Change, RoleManagersChangeTheRolesOfCourses.RowAccessSql, RowAccessRoles.User),
        RowAccessRule.For<Lab>("Seats read the labs they see", RowOperations.Read, SeatsReadTheLabsTheySee.RowAccessSql, RowAccessRoles.User),
    ];

    /// <summary>
    /// The contributions the access files are written with: the packages' own classes, Tenancy's with the catalogue
    /// the campus runs with, and Membership's once for each kind of resource with members, closed over its member
    /// class, with its rules. An application lists none of them: the Supabase build makes each one in the project
    /// that runs the export, from what it marks, <c>[TenancyCatalogue]</c> and <c>[MembershipRules&lt;TMember&gt;]</c>.
    /// This suite runs no export, so it makes the packages' classes itself, from what an application would mark.
    /// </summary>
    public static IReadOnlyList<IRowAccessContribution> Contributions =>
    [
        new TenancyRowAccessContribution(CampusCatalogue.Built),
        new MembershipRowAccessContribution<CourseTutor>(CourseMembership.Rules),
        new MembershipRowAccessContribution<LabTechnician>(LabMembership.Rules),
    ];

    /// <summary>Tenancy's context on Npgsql, in <paramref name="schemas"/> or the campus' own, for its model: it never connects.</summary>
    public static TenancyContext TenancyModel(CampusSchemas? schemas = null)
        => new((DbContextOptions<TenancyContext>)(schemas ?? CampusSchemas.Default).Configure(new DbContextOptionsBuilder<TenancyContext>().UseNpgsql("Host=model-only")).Options);

    /// <summary>The courses' context on Npgsql, in <paramref name="schemas"/> or the campus' own, for its model: it never connects.</summary>
    public static CampusContext CampusModel(CampusSchemas? schemas = null)
        => new((DbContextOptions<CampusContext>)(schemas ?? CampusSchemas.Default).Configure(new DbContextOptionsBuilder<CampusContext>().UseNpgsql("Host=model-only")).Options);

    /// <summary>
    /// The access files of the application's two contexts, in the order to run them: written together, with
    /// <paramref name="contributions"/>, or <see cref="Contributions"/>, and with <paramref name="rules"/>, or the
    /// application's own, so a function of the courses' finds Tenancy's wherever Tenancy's context puts it: in
    /// <paramref name="schemas"/>, or the campus' own.
    /// </summary>
    public static IReadOnlyList<string> AccessScripts(IReadOnlyList<IRowAccessContribution>? contributions = null, IReadOnlyList<RowAccessRule>? rules = null, CampusSchemas? schemas = null)
    {
        using var tenancy = TenancyModel(schemas);
        using var campus = CampusModel(schemas);
        return [.. PostgresRowAccess.Scripts([tenancy, campus], rules ?? Rules, [], new RowAccessExport { Contributions = contributions ?? Contributions }).Select(each => each.Script)];
    }

    /// <summary>Runs <paramref name="sql"/> on a new, unpooled connection, closed when it is done.</summary>
    public static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>
/// The campus of <see cref="CampusPostgres"/> on a Postgres of its own, in schemas Postgres keeps only in double
/// quotes (<see cref="CampusSchemas.Quoted"/>): what a host that names its schemas <c>"Tenancy"</c> and
/// <c>"Campus_Life"</c> runs on. Started the first time a test asks for it, and only then.
/// </summary>
public sealed class QuotedCampusPostgres() : CampusPostgresServer(CampusSchemas.Quoted);

/// <summary>
/// The schemas the campus' two contexts are in: Tenancy's, and the courses' and the labs'. The campus' own, which
/// its contexts name themselves, or ones that need quotes, which a host names in their place.
/// </summary>
public sealed class CampusSchemas
{
    private CampusSchemas(string tenancy, string campus)
    {
        Tenancy = tenancy;
        Campus = campus;
    }

    /// <summary>The campus' own: <c>tenancy</c> and <c>campus</c>, which Postgres keeps as SQL writes them.</summary>
    public static CampusSchemas Default { get; } = new(TenancyContext.Schema, CampusContext.Schema);

    /// <summary>
    /// <c>Tenancy</c>, with a capital, and <c>Campus_Life</c>, with a capital and an underscore: schemas Entity
    /// Framework creates in double quotes, and which Postgres would fold to lower case without them.
    /// </summary>
    public static CampusSchemas Quoted { get; } = new("Tenancy", "Campus_Life");

    /// <summary>The schema of Tenancy's context.</summary>
    public string Tenancy { get; }

    /// <summary>The schema of the courses' and the labs' context.</summary>
    public string Campus { get; }

    /// <summary>
    /// <paramref name="options"/>, with what makes the contexts build their models in these schemas: nothing for
    /// the campus' own, and for the quoted ones the model a host that names them builds (<see cref="QuotedCampusModel"/>).
    /// </summary>
    public DbContextOptionsBuilder Configure(DbContextOptionsBuilder options)
        => ReferenceEquals(this, Quoted) ? options.ReplaceService<IModelCustomizer, QuotedCampusModel>() : options;

    /// <inheritdoc />
    public override string ToString() => $"{Tenancy} and {Campus}";
}

/// <summary>
/// The campus' models as a host builds them that names their schemas <see cref="CampusSchemas.Quoted"/>: the same
/// calls the contexts make (<see cref="TenancyContext.Build"/>, <see cref="CampusContext.Build"/>), in those
/// schemas. It replaces a service of the context's options, so a context with it has a model of its own, built
/// once and cached apart from the one the same class has in the campus' own schemas.
/// </summary>
internal sealed class QuotedCampusModel(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        switch (context)
        {
            case TenancyContext:
                TenancyContext.Build(modelBuilder, context.Database, CampusSchemas.Quoted.Tenancy);
                break;
            case CampusContext:
                CampusContext.Build(modelBuilder, context.Database, CampusSchemas.Quoted.Campus, CampusSchemas.Quoted.Tenancy);
                break;
            default:
                base.Customize(modelBuilder, context);
                break;
        }
    }
}

/// <summary>
/// What <see cref="CampusPostgres"/> and <see cref="QuotedCampusPostgres"/> share: a server started the first time a
/// test asks, a template made from the campus' models in <see cref="Schemas"/>, and a database of its own for each test.
/// </summary>
/// <param name="schemas">The schemas the campus' contexts are in.</param>
public abstract class CampusPostgresServer(CampusSchemas schemas) : CampusDatabases, IAsyncLifetime
{
    private const string LoginPassword = "campus_app";
    private const string Template = "campus_template";

    /// <summary>A template cannot be copied twice at once, so every copy waits its turn.</summary>
    private static readonly SemaphoreSlim Copying = new(1, 1);

    private readonly Lock _starting = new();
    private Task<bool>? _start;
    private PostgreSqlContainer? _container;
    private string? _notStarted;
    private TimeProvider _clock = TimeProvider.System;
    private int _databases;

    /// <summary>The schemas the campus' contexts are in on this server.</summary>
    public CampusSchemas Schemas { get; } = schemas;

    /// <summary>
    /// <paramref name="schema"/> as Entity Framework's own SQL names it, a query of a context's say, quoted where
    /// Postgres needs it: <c>tenancy</c>, <c>"Tenancy"</c>.
    /// </summary>
    public string Delimited(string schema)
    {
        using var model = CampusPostgres.TenancyModel(Schemas);
        return model.GetService<ISqlGenerationHelper>().DelimitIdentifier(schema);
    }

    /// <summary>
    /// <paramref name="schema"/> as the access files and the start-up checks name it, always in double quotes:
    /// <c>"tenancy"</c>, <c>"Tenancy"</c>.
    /// </summary>
    public string Quoted(string schema) => RowAccessModel.Schema(schema);

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

    /// <inheritdoc />
    /// <remarks>Skips the test without Docker, or fails it where containers are required.</remarks>
    public override async Task<CampusApp> CreateAsync(CampusContexts contexts = CampusContexts.OfTheRequest)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var available = await StartAsync(cancellation);
        RequiredContainers.EnforceOrSkip(
            available,
            RequiredContainers.Required,
            "PostgreSQL",
            $"'{CampusPostgres.Image}' could not be started, so the two domains together are covered on SQLite alone on this machine. The start said: {_notStarted ?? "nothing"}");

        var name = $"campus_{Interlocked.Increment(ref _databases)}";
        await Copying.WaitAsync(cancellation);
        try
        {
            await CampusPostgres.ExecuteAsync(Superuser(), $"CREATE DATABASE {name} TEMPLATE {Template} OWNER {CampusPostgres.LoginRole}", cancellation);
        }
        finally
        {
            Copying.Release();
        }

        var connectionString = Login(name);
        var clock = _clock;
        var schemas = Schemas;

        // Made for the moment the test runs at, since the database's functions ask the database's clock.
        var campus = new CampusApp(
            from => new CampusServices(
                options => schemas.Configure(options.UseNpgsql(connectionString)),
                clock,
                host: host =>
                {
                    host.AddPostgresRowLevelSecurity();
                    host.AddTenancyPostgres();
                },
                wiring: (options, provider) => options.UsePostgresRowLevelSecurity(provider),
                contexts: from),
            contexts,
            new CampusScenario(clock.GetUtcNow()),
            "PostgreSQL",
            Delimited(schemas.Tenancy) + ".caller_permissions()")
        {
            ConnectionString = connectionString,
        };

        await campus.Scenario.SaveAsync(campus.Services);
        campus.Services.Commands.Reset();
        return campus;
    }

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
            Username = CampusPostgres.LoginRole,
            Password = LoginPassword,
            IncludeErrorDetail = true,
            ConnectionIdleLifetime = 10,
            ConnectionPruningInterval = 5,
        }.ConnectionString;

    /// <summary>
    /// Whether there is a server, started the first time a test asks: one start for the run, which no test's own
    /// token ends. Without Docker there is none and every test is skipped, or failed where containers are
    /// required. A server that started and could not be set up fails every test that asks, with the reason.
    /// </summary>
    private Task<bool> StartAsync(CancellationToken cancellationToken)
    {
        Task<bool> start;
        lock (_starting)
        {
            // On the pool, so the start neither runs under this lock nor comes back to the asking test's context.
            start = _start ??= Task.Run(StartServerAsync);
        }

        return start.WaitAsync(cancellationToken);
    }

    private async Task<bool> StartServerAsync()
    {
        // Every test has a database of its own, and the pools of the tests before it keep idle connections to
        // theirs open for a while: more than the default hundred. Built inside the try: the builder asks for
        // Docker already, and says it is missing by throwing.
        PostgreSqlContainer? container = null;
        try
        {
            container = new PostgreSqlBuilder(CampusPostgres.Image)
                .WithDatabase("postgres")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .WithCommand("-c", "max_connections=300")
                .Build();
            await container.StartAsync(CancellationToken.None);
        }
        catch (Exception failed)
        {
            // Kept, and said where a test is skipped or failed: no Docker, an image that could not be pulled
            // and a start that timed out are three things, and only the text tells them apart.
            _notStarted = failed.GetType().Name + ": " + failed.Message;
            if (container is not null)
            {
                await container.DisposeAsync();
            }

            return false;
        }

        _container = container;
        await CampusPostgres.ExecuteAsync(Superuser(), ServerRole.Create(CampusPostgres.LoginRole, $"LOGIN NOINHERIT PASSWORD '{LoginPassword}'"), CancellationToken.None);
        _clock = await DatabaseClock.MeasureAsync(Superuser(), CancellationToken.None);
        await PrepareTemplateAsync(CancellationToken.None);
        return true;
    }

    /// <summary>
    /// The template: its schemas made by the superuser for the login role, the setup script run by the superuser,
    /// which makes the callers' roles and the scoped system role and grants them to the login role, the tables
    /// made by the login role from the two contexts' models, with what callers may do with them: everything, so
    /// that only the policies and Tenancy's triggers stand in a caller's way; and the access files, in order. Each
    /// schema is named as Entity Framework names it, in double quotes where Postgres needs them.
    /// </summary>
    private async Task PrepareTemplateAsync(CancellationToken cancellationToken)
    {
        var login = CampusPostgres.LoginRole;
        await CampusPostgres.ExecuteAsync(Superuser(), $"CREATE DATABASE {Template} OWNER {login}", cancellationToken);

        var tenancySchema = Delimited(Schemas.Tenancy);
        var campusSchema = Delimited(Schemas.Campus);
        var owner = Superuser(Template);
        await CampusPostgres.ExecuteAsync(
            owner,
            $"CREATE SCHEMA ddd AUTHORIZATION {login}; CREATE SCHEMA {tenancySchema} AUTHORIZATION {login}; CREATE SCHEMA {campusSchema} AUTHORIZATION {login};",
            cancellationToken);
        await CampusPostgres.ExecuteAsync(owner, PostgresRowAccess.SetupScript(loginRole: login), cancellationToken);

        var asLogin = Login(Template);
        await using (var tenancy = CampusPostgres.TenancyModel(Schemas))
        await using (var campus = CampusPostgres.CampusModel(Schemas))
        {
            await CampusPostgres.ExecuteAsync(asLogin, tenancy.Database.GenerateCreateScript(), cancellationToken);
            await CampusPostgres.ExecuteAsync(asLogin, campus.Database.GenerateCreateScript(), cancellationToken);
        }

        await CampusPostgres.ExecuteAsync(
            asLogin,
            $"""
            GRANT USAGE ON SCHEMA {tenancySchema}, {campusSchema} TO authenticated, anon, ddd_system_in;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {tenancySchema}, {campusSchema} TO authenticated, anon, ddd_system_in;
            """,
            cancellationToken);

        foreach (var script in CampusPostgres.AccessScripts(schemas: Schemas))
        {
            await CampusPostgres.ExecuteAsync(asLogin, script, cancellationToken);
        }

        NpgsqlConnection.ClearAllPools();
    }

    /// <summary>
    /// The machine's clock, set to the database's: what an application has whose servers keep their clocks in
    /// step, which is what Tenancy on Postgres expects. A grant's start is stamped by the application's clock and
    /// the policies compare it with the database's <c>now()</c>, so on a database whose clock runs behind the
    /// machine's, a container's for one, a grant made a moment ago would not be live yet. It errs to the side of
    /// running behind the database, by no more than the round trip that measured it.
    /// </summary>
    private sealed class DatabaseClock(TimeSpan ahead) : TimeProvider
    {
        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + ahead;

        /// <summary>The clock of the server <paramref name="connectionString"/> names.</summary>
        public static async Task<DatabaseClock> MeasureAsync(string connectionString, CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT clock_timestamp()", connection);
            var database = new DateTimeOffset((DateTime)(await command.ExecuteScalarAsync(cancellationToken))!, TimeSpan.Zero);

            // Read after the answer came back: the database's moment lies before it, so the difference is never too large.
            return new DatabaseClock(database - System.GetUtcNow());
        }
    }
}

/// <summary>
/// A query as a caller: a raw connection as the login role, in a transaction, with the role, the claims and the
/// tenant set for that transaction alone, as the interceptors set them for a caller. What runs here goes past
/// the application, as its own SQL that forgot a condition would: only the policies, the privileges and the
/// functions' grants stand in its way. Disposing it rolls it back.
/// </summary>
public sealed class CallerSession : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;

    private CallerSession(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    /// <summary>
    /// Begins a session as a signed-in person: the claims of their token, the role of a signed-in user, and the
    /// tenant the application would name for the request, which is the application's word and not the caller's.
    /// </summary>
    /// <param name="connectionString">The test's database, as the login role.</param>
    /// <param name="person">Who signed in.</param>
    /// <param name="tenant">The tenant the connection names, or <see langword="null"/> for none.</param>
    public static async Task<CallerSession> BeginAsync(string connectionString, CampusPerson person, TenantId? tenant)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var session = new CallerSession(connection, await connection.BeginTransactionAsync(TestContext.Current.CancellationToken));

        await using var command = new NpgsqlCommand(
            "SELECT set_config('role', $1, true), set_config('request.jwt.claims', $2, true), set_config($3, $4, true)",
            connection,
            session._transaction);
        command.Parameters.Add(new NpgsqlParameter { Value = PostgresRowLevelSecurityOptions.AuthenticatedRole });
        command.Parameters.Add(new NpgsqlParameter { Value = person.Claims });
        command.Parameters.Add(new NpgsqlParameter { Value = TenancyRowLevelSecurity.TenantSetting });
        command.Parameters.Add(new NpgsqlParameter { Value = tenant is { } named ? named.Value.ToString("D") : string.Empty });
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return session;
    }

    /// <summary>The first column of every row <paramref name="sql"/> answers, in the order it answers them.</summary>
    public async Task<List<T>> ListAsync<T>(string sql, params object[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, _connection, _transaction);
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
    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
