using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DDDToolkit.Auth.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using Examples.Tenancy.AppHost;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

[assembly: AssemblyFixture(typeof(Examples.Tenancy.Tests.Infrastructure.SampleSupabaseStack))]

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// Supabase as the sample meets it, on Supabase's own images (<see cref="SupabaseImages"/>): its Postgres, and
/// for the tests that ask, its Auth server with a mail catcher behind it. One stack for every test of the run,
/// started the first time a test asks for a part of it, so a run of the tests that need no Docker starts
/// nothing, and stopped with the run.
/// </summary>
/// <remarks>
/// <para>
/// <b>What a test gets.</b> <see cref="CreateDatabaseAsync"/> gives a database of the test's own that is what
/// a new project's database is: the image's roles, schemas and extensions, and the <c>auth</c> schema as the
/// Auth server's migrations leave it. <see cref="AuthAsync"/> gives the Auth server, which keeps its users in
/// the image's own database, <c>postgres</c>, as it does in a project. A test whose database has to be
/// migrated first asks for a copy of one that was (<see cref="CreateDatabaseFromAsync"/>): the migrations
/// then run once in the run, not once per test. Such a database may itself start as a copy of another that
/// was filled (<see cref="FilledAsync"/>), which is how the sample's demonstration is seeded once in a run.
/// </para>
/// <para>
/// <b>Why Auth's migrations run before the first database is handed out.</b> The image alone is not yet a
/// project's database: its <c>auth.uid()</c> and <c>auth.role()</c> read the older setting for each claim and
/// know nothing of <c>request.jwt.claims</c>, and it has no <c>auth.jwt()</c> at all. The Auth server brings
/// them to what a project has when it migrates its schema, which is the first thing it does there. So the
/// stack runs those migrations once, with the Auth image itself, whether or not a test asks for the server.
/// </para>
/// <para>
/// <b>Why a template.</b> The image's database cannot be copied as it runs: two of its background workers
/// stay connected to it, and Postgres copies no database anybody is connected to. So once, after Auth's
/// migrations, the stack closes that database to connections for the moment the copy takes, and every test's
/// database is then a copy of that copy. A copy takes everything inside the database and nothing Postgres
/// keeps beside it: the right to make schemas there, which the image gives a few of Supabase's own service
/// roles on its database, is not on a copy. No test connects as one of those.
/// </para>
/// <para>
/// <b>Who does what.</b> A test works as <see cref="MigrationRole"/>, the role a project's migrations run
/// as, which on this image is no superuser. The image's superuser is used for two things only, where the
/// stack stands in for the platform: giving the Auth server's database role a password, and making the
/// template.
/// </para>
/// <para>
/// <b>What is not here.</b> The Storage and Realtime servers: the image has their schemas and none of their
/// tables, which each server makes when it migrates.
/// </para>
/// </remarks>
public sealed class SampleSupabaseStack : IAsyncLifetime
{
    /// <summary>
    /// The project's URL as the tokens name it: Auth signs with <c>{ProjectUrl}/auth/v1</c> as the issuer, the
    /// one the sample's host checks on a developer's machine. Nothing listens there; the Auth server itself
    /// answers at <see cref="SupabaseAuthServer.Url"/>.
    /// </summary>
    public const string ProjectUrl = "http://127.0.0.1:54321";

    /// <summary>
    /// The secret of Supabase's local stack, which is public knowledge and is what the host's development
    /// settings carry. The dev login signs its tokens with it, and Auth reads the service role's token with
    /// it. A person's token this Auth server signs with a key of its own, which it publishes
    /// (<see cref="AuthSigningKeys"/>), as the Auth server of Supabase's own local stack does: the host checks
    /// each kind with its own kind of key.
    /// </summary>
    public const string JwtSecret = "super-secret-jwt-token-with-at-least-32-characters-long";

    /// <summary>
    /// The test collection of every class that makes or changes the demonstration people at the stack's Auth
    /// server. Auth has one set of users for the run, whichever database a test works in, so those classes take
    /// turns: one that gives a person a password must not be overtaken by another that gives them a different one.
    /// </summary>
    public const string AuthUsers = "The demonstration people at the stack's Auth server";

    /// <summary>The role migrations run as on Supabase, and the role a test's connection logs in as.</summary>
    public const string MigrationRole = "postgres";

    /// <summary>The image's superuser, which a project's owner never gets to be.</summary>
    private const string Superuser = "supabase_admin";

    /// <summary>The role the Auth server works in the database as, and the owner of everything in <c>auth</c>.</summary>
    private const string AuthRole = "supabase_auth_admin";

    /// <summary>The role Supabase gives a signed-in user, in the database and in their tokens.</summary>
    private const string UserRole = "authenticated";

    /// <summary>The image's own database, where a project's data is and where Auth keeps its users.</summary>
    private const string ProjectDatabase = "postgres";

    /// <summary>The copy of <see cref="ProjectDatabase"/> every test's database is made from.</summary>
    private const string ProjectTemplate = "project_template";

    private const string DatabaseHost = "db";
    private const string MailHost = "mail";
    private const int PostgresPort = 5432;
    private const int AuthPort = 9999;
    private const int SmtpPort = 1025;
    private const int MailApiPort = 8025;

    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(3);

    /// <summary>A template cannot be copied twice at once, so every copy waits its turn.</summary>
    private static readonly SemaphoreSlim Copying = new(1, 1);

    /// <summary>
    /// The most connections the server takes. The image's own setting is for one application; a run has dozens
    /// of hosts on this one server at once, a few connections each, and they have to fit.
    /// </summary>
    private const int MaxConnections = 400;

    private readonly ConcurrentStack<IAsyncDisposable> _started = new();
    private readonly ConcurrentDictionary<string, Lazy<Task>> _filled = new();
    private readonly HttpClient _http = new();
    private readonly Lazy<Task<Server?>> _server;
    private readonly Lazy<Task<SupabaseAuthServer>> _auth;
    private string? _whyNot;
    private int _databases;

    public SampleSupabaseStack()
    {
        // Started once, by whichever test asks first, and not on that test's cancellation token: a test that is
        // cancelled while the stack starts must not leave the others a stack that is half there.
        _server = new(() => Task.Run(StartServerAsync));
        _auth = new(() => Task.Run(StartAuthAsync));
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        // Whatever is still starting has to be there before it can be taken away.
        foreach (var starting in new Task?[] { _server.IsValueCreated ? _server.Value : null, _auth.IsValueCreated ? _auth.Value : null })
        {
            try
            {
                await (starting ?? Task.CompletedTask);
            }
            catch (Exception)
            {
                // It failed to start, which its tests have reported; what did start is removed below.
            }
        }

        // Last started, first removed: the network goes after everything on it.
        while (_started.TryPop(out var resource))
        {
            await resource.DisposeAsync();
        }

        _http.Dispose();
    }

    /// <summary>
    /// A new database of the calling test's own, as a new project's is, with a connection for
    /// <see cref="MigrationRole"/>. Skips the test without Docker, or fails it where containers are required.
    /// </summary>
    public async Task<SupabaseDatabase> CreateDatabaseAsync(CancellationToken cancellationToken)
        => await CopyAsync(await ServerAsync(cancellationToken), ProjectTemplate, cancellationToken);

    /// <summary>
    /// A new database of the calling test's own that is a copy of one <paramref name="fill"/> filled: a new
    /// project's database with, say, an application's migrations applied. The first test that asks for
    /// <paramref name="filled"/> has it made and filled, once for the run; every test, that one included, gets
    /// a copy of it. Skips the test without Docker, or fails it where containers are required.
    /// </summary>
    /// <remarks>
    /// Besides the time it saves, this keeps what a migration does to the server, not to a database, to one
    /// time and one at a time: roles are the server's, and two databases migrated at once would each make and
    /// change the same roles, which Postgres refuses the slower of. A copy takes everything inside the database,
    /// what roles own and may do there included.
    /// </remarks>
    /// <param name="filled">The name of the database that is filled and copied from: lower case letters, digits and <c>_</c>.</param>
    /// <param name="fill">Fills it, as <see cref="MigrationRole"/>. It leaves nobody connected, or no copy can be made.</param>
    /// <param name="cancellationToken">Cancels the calling test's wait and its copy, never the filling.</param>
    /// <param name="startingFrom">What <see cref="FilledAsync"/> answered for the database it starts as a copy of; a new project's database when left out.</param>
    public async Task<SupabaseDatabase> CreateDatabaseFromAsync(string filled, Func<SupabaseDatabase, Task> fill, CancellationToken cancellationToken, string? startingFrom = null)
        => await CopyAsync(await ServerAsync(cancellationToken), await FilledAsync(filled, fill, cancellationToken, startingFrom), cancellationToken);

    /// <summary>
    /// The database <paramref name="filled"/>, made and filled by the first test that asks, once for the run, and
    /// handed to nobody: it is there to be copied. Answers its name once it is filled, which is what another
    /// filled database names to start as a copy of this one. Skips the test without Docker, or fails it where
    /// containers are required.
    /// </summary>
    /// <param name="filled">The name of the database: lower case letters, digits and <c>_</c>.</param>
    /// <param name="fill">Fills it, as <see cref="MigrationRole"/>. It leaves nobody connected, or no copy can be made.</param>
    /// <param name="cancellationToken">Cancels the calling test's wait, never the filling.</param>
    /// <param name="startingFrom">What an earlier call answered for the database this one starts as a copy of; a new project's database when left out.</param>
    public async Task<string> FilledAsync(string filled, Func<SupabaseDatabase, Task> fill, CancellationToken cancellationToken, string? startingFrom = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filled);
        ArgumentNullException.ThrowIfNull(fill);
        var server = await ServerAsync(cancellationToken);

        // Filled once, by whichever test asks first, and not on that test's cancellation token: a test that is
        // cancelled while it fills must not leave the others a database that is half there.
        await _filled.GetOrAdd(filled, name => new Lazy<Task>(() => Task.Run(async () =>
            await fill(await CopyAsync(server, startingFrom ?? ProjectTemplate, CancellationToken.None, name))))).Value.WaitAsync(cancellationToken);

        return filled;
    }

    /// <summary>
    /// The Auth server, started with its mail catcher the first time a test asks for it. Skips the test without
    /// Docker, or fails it where containers are required.
    /// </summary>
    public async Task<SupabaseAuthServer> AuthAsync(CancellationToken cancellationToken)
    {
        await ServerAsync(cancellationToken);
        return await _auth.Value.WaitAsync(cancellationToken);
    }

    /// <summary>Runs <paramref name="sql"/> as one statement on a new connection, closed when it is done.</summary>
    public static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>A copy of <paramref name="template"/>, under <paramref name="name"/> or the next name of the run, when its turn comes.</summary>
    private async Task<SupabaseDatabase> CopyAsync(Server server, string template, CancellationToken cancellationToken, string? name = null)
    {
        name ??= $"stack_{Interlocked.Increment(ref _databases)}";
        await Copying.WaitAsync(cancellationToken);
        try
        {
            await ExecuteAsync(server.ConnectionString(MigrationRole, ProjectDatabase), $"CREATE DATABASE {name} TEMPLATE {template}", cancellationToken);
        }
        finally
        {
            Copying.Release();
        }

        return new SupabaseDatabase(name, server.ConnectionString(MigrationRole, name));
    }

    private async Task<Server> ServerAsync(CancellationToken cancellationToken)
    {
        var server = await _server.Value.WaitAsync(cancellationToken);
        RequiredContainers.EnforceOrSkip(
            server is not null,
            RequiredContainers.Required,
            "Supabase's Postgres image",
            $"'{SupabaseImages.Postgres}' could not be started, as on a machine without Docker ({_whyNot}). What the sample relies on from Supabase's own images is not covered here.");
        return server!;
    }

    /// <summary>
    /// The Postgres container, Auth's migrations and the template. <see langword="null"/> when the container
    /// could not be started, which is what a machine without Docker looks like; anything that fails after that
    /// is a failure of every test that asks, with its reason.
    /// </summary>
    private async Task<Server?> StartServerAsync()
    {
        var cancellation = CancellationToken.None;
        var password = NewPassword();
        INetwork network;
        IContainer container;
        try
        {
            network = new NetworkBuilder().Build();
            _started.Push(network);
            await network.CreateAsync(cancellation);

            // The image's own command, spelled out to add to it: the directory it names holds Supabase's
            // settings file, which is where Supabase loads its extensions and guards its roles, so a command
            // that left it out would start a plain Postgres in Supabase's clothes. Functions are tracked, so a
            // test can count how often one was called. The connections are for every host of the run at once.
            // The last three trade durability for speed, as a server that lives for one test run can.
            container = new ContainerBuilder(SupabaseImages.Postgres)
                .WithNetwork(network)
                .WithNetworkAliases(DatabaseHost)
                .WithEnvironment("POSTGRES_PASSWORD", password)
                .WithPortBinding(PostgresPort, assignRandomHostPort: true)
                .WithCommand(
                    "postgres",
                    "-D", "/etc/postgresql",
                    "-c", "track_functions=all",
                    "-c", "max_connections=" + MaxConnections.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "-c", "fsync=off",
                    "-c", "synchronous_commit=off",
                    "-c", "full_page_writes=off")

                // Over TCP, which the server that runs the image's own set-up scripts does not listen on: this
                // answers only once those are done and the real server is up.
                .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted(
                    ["pg_isready", "-U", MigrationRole, "-h", "localhost"],
                    wait => wait.WithTimeout(StartTimeout)))
                .Build();
            _started.Push(container);
            await container.StartAsync(cancellation);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _whyNot = exception.Message.Trim();
            return null;
        }

        var server = new Server(network, container.Hostname, container.GetMappedPublicPort(PostgresPort), password, NewPassword(), AuthSigningKeys.New());

        // The image makes the Auth server's role and gives it no password; a project's platform does that, and
        // only a superuser can, because the role is one Supabase reserves.
        await ExecuteAsync(server.ConnectionString(Superuser, ProjectDatabase), $"ALTER ROLE {AuthRole} WITH PASSWORD '{server.AuthPassword}'", cancellation);

        await MigrateAuthAsync(server, cancellation);
        await MakeTemplateAsync(server, cancellation);
        return server;
    }

    /// <summary>
    /// Auth's own migrations, run by the Auth image against the image's database. Nothing of the Auth server is
    /// left running: the container migrates and exits.
    /// </summary>
    private static async Task MigrateAuthAsync(Server server, CancellationToken cancellationToken)
    {
        await using var migration = new ContainerBuilder(SupabaseImages.Auth)
            .WithNetwork(server.Network)
            .WithEnvironment(AuthSettings(server))
            .WithCommand("auth", "migrate")
            .Build();

        await migration.StartAsync(cancellationToken);
        var exitCode = await migration.GetExitCodeAsync(cancellationToken).WaitAsync(StartTimeout, cancellationToken);
        if (exitCode != 0)
        {
            var (output, errors) = await migration.GetLogsAsync(ct: cancellationToken);
            throw new InvalidOperationException(
                $"'{SupabaseImages.Auth}' could not migrate the auth schema of '{SupabaseImages.Postgres}' (exit code {exitCode}), so no database of this stack would be a project's.\n{output}\n{errors}");
        }
    }

    /// <summary>
    /// Copies the image's database, as Auth's migrations left it, into the template. The database is closed to
    /// connections while it is copied, because the image's background workers come back to it within a second
    /// of being sent away; they find it again as soon as it is open.
    /// </summary>
    private static async Task MakeTemplateAsync(Server server, CancellationToken cancellationToken)
    {
        // From another database: the one that is copied may have nobody in it, the copier included. One
        // statement per command, since a database is not made inside a transaction.
        await using var connection = new NpgsqlConnection(server.ConnectionString(Superuser, "template1"));
        await connection.OpenAsync(cancellationToken);

        await RunAsync($"ALTER DATABASE {ProjectDatabase} ALLOW_CONNECTIONS false");
        try
        {
            await RunAsync($"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{ProjectDatabase}' AND pid <> pg_backend_pid()");
            await RunAsync($"CREATE DATABASE {ProjectTemplate} TEMPLATE {ProjectDatabase} OWNER {MigrationRole} IS_TEMPLATE true");
        }
        finally
        {
            await RunAsync($"ALTER DATABASE {ProjectDatabase} ALLOW_CONNECTIONS true");
        }

        async Task RunAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>The mail catcher and the Auth server in front of it, on the network the database is on.</summary>
    private async Task<SupabaseAuthServer> StartAuthAsync()
    {
        var cancellation = CancellationToken.None;
        var server = await _server.Value
            ?? throw new InvalidOperationException("The Auth server was asked for before its database was there.");

        var mail = new ContainerBuilder(SupabaseImages.MailCatcher)
            .WithNetwork(server.Network)
            .WithNetworkAliases(MailHost)
            .WithPortBinding(MailApiPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
                request => request.ForPort(MailApiPort).ForPath("/readyz"),
                wait => wait.WithTimeout(StartTimeout)))
            .Build();
        _started.Push(mail);
        await mail.StartAsync(cancellation);

        var auth = new ContainerBuilder(SupabaseImages.Auth)
            .WithNetwork(server.Network)
            .WithEnvironment(AuthSettings(server))
            .WithPortBinding(AuthPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
                request => request.ForPort(AuthPort).ForPath("/health"),
                wait => wait.WithTimeout(StartTimeout)))
            .Build();
        _started.Push(auth);
        await auth.StartAsync(cancellation);

        return new SupabaseAuthServer(
            new Uri($"http://{auth.Hostname}:{auth.GetMappedPublicPort(AuthPort)}/"),
            ServiceRoleKey(),
            new MailCatcher(_http, new Uri($"http://{mail.Hostname}:{mail.GetMappedPublicPort(MailApiPort)}/")));
    }

    /// <summary>
    /// The Auth server's settings, for its migrations and for the server alike: it reads all of them before it
    /// does either.
    /// </summary>
    private static Dictionary<string, string> AuthSettings(Server server) => new()
    {
        ["GOTRUE_API_HOST"] = "0.0.0.0",
        ["GOTRUE_API_PORT"] = AuthPort.ToString(System.Globalization.CultureInfo.InvariantCulture),

        // As the role Supabase gives it, in the image's own database.
        ["GOTRUE_DB_DRIVER"] = "postgres",
        ["GOTRUE_DB_DATABASE_URL"] = $"postgres://{AuthRole}:{server.AuthPassword}@{DatabaseHost}:{PostgresPort}/{ProjectDatabase}",

        // Tokens as Supabase's own local stack signs them: the project's URL in the issuer, and a signing key
        // whose public half Auth publishes, so a person's token is checked with a published key here as it is
        // on that stack and on a hosted project. The secret still reads the service role's token.
        ["API_EXTERNAL_URL"] = ProjectUrl,
        ["GOTRUE_JWT_ISSUER"] = SupabaseTokens.IssuerOf(ProjectUrl),
        ["GOTRUE_JWT_SECRET"] = JwtSecret,
        [AuthSigningKeys.Setting] = server.AuthKeys,
        [AuthSigningKeys.MethodsSetting] = AuthSigningKeys.Methods,
        ["GOTRUE_JWT_AUD"] = SupabaseTokens.Audience,
        ["GOTRUE_JWT_EXP"] = "3600",
        ["GOTRUE_JWT_ADMIN_ROLES"] = "service_role",

        // The role a new user gets, and with it the role claim of every token of theirs. Without it a user the
        // admin API makes has none, and a token without a role is nobody's. Auth warns that it will stop reading
        // this setting; it reads it still, and Supabase's own local stack sets it.
        ["GOTRUE_JWT_DEFAULT_GROUP_NAME"] = UserRole,

        // Nobody signs up: a person gets in because the application invited them. Signing in is by e-mail
        // address, which has to be proven first, and never anonymous. The site is the sample's UI, as in the
        // sample's supabase/config.toml: Auth sends a browser on only to an address on that host.
        ["GOTRUE_SITE_URL"] = "http://localhost:5091",
        ["GOTRUE_DISABLE_SIGNUP"] = "true",
        ["GOTRUE_EXTERNAL_EMAIL_ENABLED"] = "true",
        ["GOTRUE_MAILER_AUTOCONFIRM"] = "false",
        ["GOTRUE_EXTERNAL_ANONYMOUS_USERS_ENABLED"] = "false",

        // Mail goes to the catcher, with links that lead where a project's lead. The allowance of mails per
        // hour is for one project's real people; here it is every test of the run, so it is taken away.
        ["GOTRUE_SMTP_HOST"] = MailHost,
        ["GOTRUE_SMTP_PORT"] = SmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["GOTRUE_SMTP_ADMIN_EMAIL"] = "tenancy@example.test",
        ["GOTRUE_SMTP_SENDER_NAME"] = "Tenancy sample",
        ["GOTRUE_RATE_LIMIT_EMAIL_SENT"] = "100000",
        ["GOTRUE_MAILER_URLPATHS_INVITE"] = "/auth/v1/verify",
        ["GOTRUE_MAILER_URLPATHS_CONFIRMATION"] = "/auth/v1/verify",
        ["GOTRUE_MAILER_URLPATHS_RECOVERY"] = "/auth/v1/verify",
        ["GOTRUE_MAILER_URLPATHS_EMAIL_CHANGE"] = "/auth/v1/verify",
    };

    /// <summary>
    /// A token with the service role, signed with the stack's secret: what the admin API takes on an Auth server
    /// with no gateway in front of it, where a project has its secret key. The gateway of the stack the Supabase
    /// CLI starts lets it through to Auth as well.
    /// </summary>
    public static string ServiceRoleKey()
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "supabase",
            IssuedAt = now,
            Expires = now.AddDays(1),
            Claims = new Dictionary<string, object> { ["role"] = "service_role" },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>A password for this run alone: nothing that opens the stack is written down anywhere.</summary>
    private static string NewPassword() => Guid.NewGuid().ToString("N");

    /// <summary>The Postgres container as the rest of the stack reaches it.</summary>
    /// <param name="Network">The network the stack's containers find each other on.</param>
    /// <param name="Host">Where the test process reaches the server.</param>
    /// <param name="Port">The port there.</param>
    /// <param name="Password">The password of the migration role and of the image's superuser.</param>
    /// <param name="AuthPassword">The password the stack gave the Auth server's role.</param>
    /// <param name="AuthKeys">The key the Auth server signs a person's token with, for this run alone.</param>
    private sealed record Server(INetwork Network, string Host, int Port, string Password, string AuthPassword, string AuthKeys)
    {
        /// <summary>
        /// As <paramref name="role"/>, to <paramref name="database"/>, without a pool: a pooled connection stays
        /// connected after it is closed, and nobody may be connected to a database while it is copied.
        /// </summary>
        public string ConnectionString(string role, string database) => new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = database,
            Username = role,
            Password = Password,
            Pooling = false,
            IncludeErrorDetail = true,
        }.ConnectionString;
    }
}

/// <summary>A database of a test's own on the stack's Postgres, as a new project's database is.</summary>
/// <param name="Name">The database's name.</param>
/// <param name="AsMigrationRole">
/// A connection string for <see cref="SampleSupabaseStack.MigrationRole"/>, without a pool: a pooled connection
/// stays connected after it is closed, and nobody may be connected to a database while it is copied, or be
/// logged in as a role while it is dropped.
/// </param>
public sealed record SupabaseDatabase(string Name, string AsMigrationRole)
{
    /// <summary>A connection string to this database for another role that can log in, again without a pool.</summary>
    public string As(string role, string password)
        => new NpgsqlConnectionStringBuilder(AsMigrationRole) { Username = role, Password = password }.ConnectionString;
}

/// <summary>The stack's Auth server.</summary>
/// <param name="Url">
/// Where it answers, with nothing in front of it: what a project has at <c>{project URL}/auth/v1/</c>.
/// </param>
/// <param name="ServiceRoleKey">A token with the service role, which the admin API takes here in place of a project's secret key.</param>
/// <param name="Mail">What it mailed.</param>
public sealed record SupabaseAuthServer(Uri Url, string ServiceRoleKey, MailCatcher Mail)
{
    /// <summary>An admin client for this server, the caller's to dispose.</summary>
    public SupabaseAuthAdmin Admin() => new(Url.ToString(), ServiceRoleKey);

    /// <summary>
    /// The person opens the link of <paramref name="mail"/>, which proves the address is theirs. Answers the
    /// access token Auth signs them in with.
    /// </summary>
    public async Task<string> FollowLinkAsync(CaughtMail mail, CancellationToken cancellationToken)
    {
        // Auth sends the browser on to the application's page, with the session in the fragment.
        var landing = await LandingOfAsync(mail, cancellationToken);
        var session = Regex.Match(landing, "[#&]access_token=(?<token>[^&]+)");
        return session.Success
            ? session.Groups["token"].Value
            : throw new InvalidOperationException("Auth answered the link with no session: " + landing);
    }

    /// <summary>
    /// The person opens the link of <paramref name="mail"/>. Answers the address Auth sends their browser on
    /// to, as Auth wrote it: the application's page, and after the <c>#</c> what the page finds there.
    /// </summary>
    public async Task<string> LandingOfAsync(CaughtMail mail, CancellationToken cancellationToken)
    {
        // The link names the project's URL, where a gateway would pass it on to Auth. Here the test is the gateway.
        var link = Regex.Match(mail.Text, Regex.Escape(SupabaseTokens.IssuerOf(SampleSupabaseStack.ProjectUrl)) + @"/(?<rest>verify\?[^\s)]+)");
        if (!link.Success)
        {
            throw new InvalidOperationException("The mail has no link to follow: " + mail.Text);
        }

        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
        using var browser = new HttpClient(handler);
        using var answer = await browser.GetAsync(new Uri(Url, WebUtility.HtmlDecode(link.Groups["rest"].Value)), cancellationToken);

        return answer.StatusCode == HttpStatusCode.SeeOther && answer.Headers.Location is { } location
            ? location.OriginalString
            : throw new InvalidOperationException($"Auth answered the link with {(int)answer.StatusCode} and sent the browser nowhere.");
    }
}

/// <summary>The mails the stack's Auth server sent, as its mail catcher kept them.</summary>
public sealed class MailCatcher(HttpClient http, Uri url)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The mails sent to <paramref name="address"/>, the latest first, once there are at least
    /// <paramref name="atLeast"/> of them, or the ones that arrived when waiting any longer made no sense.
    /// </summary>
    public async Task<IReadOnlyList<CaughtMail>> SentToAsync(string address, int atLeast, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (true)
        {
            var mails = await FindAsync(address, cancellationToken);
            if (mails.Count >= atLeast || DateTime.UtcNow >= deadline)
            {
                return mails;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }

    private async Task<IReadOnlyList<CaughtMail>> FindAsync(string address, CancellationToken cancellationToken)
    {
        var found = await http.GetFromJsonAsync<JsonElement>(new Uri(url, "api/v1/search?query=" + Uri.EscapeDataString("to:" + address)), cancellationToken);

        var mails = new List<CaughtMail>();
        foreach (var summary in found.GetProperty("messages").EnumerateArray())
        {
            // The search matches loosely; the mail is this address's only when it is among those it was sent to.
            if (!summary.GetProperty("To").EnumerateArray().Any(to => string.Equals(to.GetProperty("Address").GetString(), address, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var mail = await http.GetFromJsonAsync<JsonElement>(new Uri(url, "api/v1/message/" + summary.GetProperty("ID").GetString()), cancellationToken);
            mails.Add(new CaughtMail(
                mail.GetProperty("From").GetProperty("Address").GetString() ?? "",
                mail.GetProperty("Subject").GetString() ?? "",
                mail.GetProperty("Text").GetString() ?? ""));
        }

        return mails;
    }
}

/// <summary>A mail the catcher kept.</summary>
/// <param name="From">The address it came from.</param>
/// <param name="Subject">Its subject.</param>
/// <param name="Text">Its body, as plain text.</param>
public sealed record CaughtMail(string From, string Subject, string Text);
