using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using Examples.Tenancy.AppHost;
using DDDToolkit.Identity;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

[assembly: AssemblyFixture(typeof(DDDToolkit.Auth.Supabase.Tests.Infrastructure.SupabaseAuthStack))]

namespace DDDToolkit.Auth.Supabase.Tests.Infrastructure;

/// <summary>
/// Supabase Auth itself, for the few tests whose question a stand-in cannot answer because the answer is
/// Auth's own: which of two requests for one address it lets through, what it mails, and who gets in
/// afterwards. It is Supabase's Auth server on Supabase's Postgres, with a mail catcher behind it, from the
/// images in <see cref="SupabaseImages"/>, which are the ones Supabase's own local stack starts.
/// </summary>
/// <remarks>
/// <para>
/// One stack for the run, started the first time a test asks for it, so the tests over
/// <see cref="StubAuthServer"/>, which are nearly all of them, start nothing and need no Docker.
/// </para>
/// <para>
/// <b>Anybody can sign up here.</b> That is the setting under which an invitation has something to get
/// wrong: a stranger can register somebody else's address and choose its password before the application
/// invites it. A project that only admits invited people switches sign-ups off, and has less to prove.
/// </para>
/// <para>
/// The Auth server migrates its own schema when it starts. The image's superuser is used for one thing,
/// where the stack stands in for the platform: giving the Auth server's database role a password.
/// </para>
/// </remarks>
public sealed class SupabaseAuthStack : IAsyncLifetime
{
    /// <summary>
    /// The project's URL as the mails and the tokens name it. Nothing listens there: a test that follows a
    /// link from a mail sends its path and query to the Auth server itself, and whoever checks a token is told
    /// where the Auth server answers.
    /// </summary>
    public const string ProjectUrl = "http://127.0.0.1:54321";

    /// <summary>The path a project's gateway has Auth under, which the links in its mails carry.</summary>
    private const string GatewayPath = "/auth/v1";

    /// <summary>
    /// The stack's secret, which opens nothing outside it. Auth reads the service role's token with it; a
    /// person's token it signs with a key of its own (<see cref="AuthSigningKeys"/>), and publishes that key.
    /// </summary>
    private const string JwtSecret = "a-secret-for-the-auth-tests-of-at-least-32-characters";

    /// <summary>The image's superuser, which a project's owner never gets to be.</summary>
    private const string Superuser = "supabase_admin";

    /// <summary>The role the Auth server works in the database as.</summary>
    private const string AuthRole = "supabase_auth_admin";

    /// <summary>The image's own database, where Auth keeps its users.</summary>
    private const string Database = "postgres";

    private const string DatabaseHost = "db";
    private const string MailHost = "mail";
    private const int PostgresPort = 5432;
    private const int AuthPort = 9999;
    private const int SmtpPort = 1025;
    private const int MailApiPort = 8025;

    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(3);

    private readonly ConcurrentStack<IAsyncDisposable> _started = new();
    private readonly HttpClient _http = new();
    private readonly Lazy<Task<SupabaseAuthServer?>> _auth;
    private string? _whyNot;

    public SupabaseAuthStack()
    {
        // Started once, by whichever test asks first, and not on that test's cancellation token: a test that
        // is cancelled while the stack starts must not leave the others a stack that is half there.
        _auth = new(() => Task.Run(StartAsync));
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_auth.IsValueCreated)
        {
            try
            {
                await _auth.Value;
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
    /// The Auth server, started with its database and its mail catcher the first time a test asks. Skips the
    /// test without Docker, or fails it where containers are required.
    /// </summary>
    public async Task<SupabaseAuthServer> AuthAsync(CancellationToken cancellationToken)
    {
        var auth = await _auth.Value.WaitAsync(cancellationToken);
        RequiredContainers.EnforceOrSkip(
            auth is not null,
            RequiredContainers.Required,
            "Supabase's Postgres image",
            $"'{SupabaseImages.Postgres}' could not be started, as on a machine without Docker ({_whyNot}). What an invitation does at Supabase's own Auth server is not covered here.");
        return auth!;
    }

    /// <summary>
    /// The three containers. <see langword="null"/> when the first could not be started, which is what a
    /// machine without Docker looks like; anything that fails after that is a failure of every test that
    /// asks, with its reason.
    /// </summary>
    private async Task<SupabaseAuthServer?> StartAsync()
    {
        var cancellation = CancellationToken.None;
        var password = NewPassword();
        var authPassword = NewPassword();

        INetwork network;
        IContainer database;
        try
        {
            network = new NetworkBuilder().Build();
            _started.Push(network);
            await network.CreateAsync(cancellation);

            // The image's own command, spelled out to add to it: the directory it names holds Supabase's
            // settings file. The last three trade durability for speed, as a server that lives for one test
            // run can.
            database = new ContainerBuilder(SupabaseImages.Postgres)
                .WithNetwork(network)
                .WithNetworkAliases(DatabaseHost)
                .WithEnvironment("POSTGRES_PASSWORD", password)
                .WithPortBinding(PostgresPort, assignRandomHostPort: true)
                .WithCommand(
                    "postgres",
                    "-D", "/etc/postgresql",
                    "-c", "fsync=off",
                    "-c", "synchronous_commit=off",
                    "-c", "full_page_writes=off")

                // Over TCP, which the server that runs the image's own set-up scripts does not listen on:
                // this answers only once those are done and the real server is up.
                .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted(
                    ["pg_isready", "-U", Superuser, "-h", "localhost"],
                    wait => wait.WithTimeout(StartTimeout)))
                .Build();
            _started.Push(database);
            await database.StartAsync(cancellation);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _whyNot = exception.Message.Trim();
            return null;
        }

        // The image makes the Auth server's role and gives it no password; a project's platform does that,
        // and only a superuser can, because the role is one Supabase reserves.
        var asSuperuser = new NpgsqlConnectionStringBuilder
        {
            Host = database.Hostname,
            Port = database.GetMappedPublicPort(PostgresPort),
            Database = Database,
            Username = Superuser,
            Password = password,
            Pooling = false,
        };
        await using (var connection = new NpgsqlConnection(asSuperuser.ConnectionString))
        {
            await connection.OpenAsync(cancellation);
            await using var command = new NpgsqlCommand($"ALTER ROLE {AuthRole} WITH PASSWORD '{authPassword}'", connection);
            await command.ExecuteNonQueryAsync(cancellation);
        }

        var mail = new ContainerBuilder(SupabaseImages.MailCatcher)
            .WithNetwork(network)
            .WithNetworkAliases(MailHost)
            .WithPortBinding(MailApiPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
                request => request.ForPort(MailApiPort).ForPath("/readyz"),
                wait => wait.WithTimeout(StartTimeout)))
            .Build();
        _started.Push(mail);
        await mail.StartAsync(cancellation);

        // The Auth server brings its schema up to date before it answers, so being healthy means migrated.
        var auth = new ContainerBuilder(SupabaseImages.Auth)
            .WithNetwork(network)
            .WithEnvironment(AuthSettings(authPassword))
            .WithPortBinding(AuthPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
                request => request.ForPort(AuthPort).ForPath("/health"),
                wait => wait.WithTimeout(StartTimeout)))
            .Build();
        _started.Push(auth);
        await auth.StartAsync(cancellation);

        return new SupabaseAuthServer(
            _http,
            new Uri($"http://{auth.Hostname}:{auth.GetMappedPublicPort(AuthPort)}/"),
            ServiceRoleKey(),
            new MailCatcher(_http, new Uri($"http://{mail.Hostname}:{mail.GetMappedPublicPort(MailApiPort)}/")),
            ProjectUrl + GatewayPath);
    }

    private static Dictionary<string, string> AuthSettings(string authPassword) => new()
    {
        ["GOTRUE_API_HOST"] = "0.0.0.0",
        ["GOTRUE_API_PORT"] = AuthPort.ToString(CultureInfo.InvariantCulture),

        // As the role Supabase gives it, in the image's own database.
        ["GOTRUE_DB_DRIVER"] = "postgres",
        ["GOTRUE_DB_DATABASE_URL"] = $"postgres://{AuthRole}:{authPassword}@{DatabaseHost}:{PostgresPort}/{Database}",

        ["API_EXTERNAL_URL"] = ProjectUrl,
        ["GOTRUE_JWT_ISSUER"] = SupabaseTokens.IssuerOf(ProjectUrl),
        ["GOTRUE_JWT_SECRET"] = JwtSecret,
        ["GOTRUE_JWT_AUD"] = SupabaseTokens.Audience,
        ["GOTRUE_JWT_EXP"] = "3600",
        ["GOTRUE_JWT_ADMIN_ROLES"] = "service_role",
        ["GOTRUE_JWT_DEFAULT_GROUP_NAME"] = "authenticated",

        // A signing key, given as Supabase's own local stack gives its Auth one: a person's token is signed
        // with it and its public half is published. The secret still reads the service role's token.
        [AuthSigningKeys.Setting] = AuthSigningKeys.New(),
        [AuthSigningKeys.MethodsSetting] = AuthSigningKeys.Methods,

        // Anybody may sign up, with an address and a password of their choosing, and gets in once the address
        // is proven by a link that was mailed to it: the setting an invitation has to be safe under.
        ["GOTRUE_SITE_URL"] = "http://127.0.0.1:3000",
        ["GOTRUE_DISABLE_SIGNUP"] = "false",
        ["GOTRUE_EXTERNAL_EMAIL_ENABLED"] = "true",
        ["GOTRUE_MAILER_AUTOCONFIRM"] = "false",
        ["GOTRUE_EXTERNAL_ANONYMOUS_USERS_ENABLED"] = "false",

        // Mail goes to the catcher. The allowance of mails per hour, and the pause between two mails to one
        // address, are for one project's real people; here it is every test of the run, so both are taken away.
        ["GOTRUE_SMTP_HOST"] = MailHost,
        ["GOTRUE_SMTP_PORT"] = SmtpPort.ToString(CultureInfo.InvariantCulture),
        ["GOTRUE_SMTP_ADMIN_EMAIL"] = "accounts@example.test",
        ["GOTRUE_SMTP_SENDER_NAME"] = "Accounts",
        ["GOTRUE_SMTP_MAX_FREQUENCY"] = "1ms",
        ["GOTRUE_RATE_LIMIT_EMAIL_SENT"] = "100000",
        ["GOTRUE_MAILER_URLPATHS_INVITE"] = GatewayPath + "/verify",
        ["GOTRUE_MAILER_URLPATHS_CONFIRMATION"] = GatewayPath + "/verify",
        ["GOTRUE_MAILER_URLPATHS_RECOVERY"] = GatewayPath + "/verify",
        ["GOTRUE_MAILER_URLPATHS_EMAIL_CHANGE"] = GatewayPath + "/verify",
    };

    /// <summary>
    /// A token with the service role, signed with the stack's secret: what the admin API takes on an Auth
    /// server with no gateway in front of it, where a project has its secret key.
    /// </summary>
    private static string ServiceRoleKey()
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
}

/// <summary>The stack's Auth server, as the application's server code and as anybody on the internet reach it.</summary>
public sealed class SupabaseAuthServer
{
    private readonly HttpClient _http;
    private readonly string _serviceRoleKey;
    private readonly string _linkPrefix;

    internal SupabaseAuthServer(HttpClient http, Uri url, string serviceRoleKey, MailCatcher mail, string linkPrefix)
    {
        _http = http;
        _serviceRoleKey = serviceRoleKey;
        _linkPrefix = linkPrefix;
        Url = url;
        Mail = mail;
    }

    /// <summary>Where it answers, with nothing in front of it: what a project has at <c>{project URL}/auth/v1/</c>.</summary>
    public Uri Url { get; }

    /// <summary>What it mailed.</summary>
    public MailCatcher Mail { get; }

    /// <summary>
    /// The project as a host beside this server describes it: the URL its tokens name, and this server as where
    /// its Auth answers, since no gateway serves it under that URL.
    /// </summary>
    public SupabaseAuthOptions Project => new() { ProjectUrl = SupabaseAuthStack.ProjectUrl, AuthUrl = Url.ToString() };

    /// <summary>An admin client for this server, the caller's to dispose. The service role's token stands in for a project's secret key.</summary>
    public SupabaseAuthAdmin Admin() => new(Project, _serviceRoleKey);

    /// <summary>An admin client that sends through <paramref name="handler"/>, for a test that has something happen between two of its calls.</summary>
    public SupabaseAuthAdmin Admin(HttpMessageHandler handler) => new(Project, _serviceRoleKey, handler);

    /// <summary>The identity port over <paramref name="admin"/>, which is all application code sees.</summary>
    public static IIdentityAccounts Accounts(SupabaseAuthAdmin admin) => new SupabaseIdentityAccounts(admin);

    /// <summary>
    /// Anybody signs up with an address and a password of their choosing, as a browser does, with no key at
    /// all. Auth answers the same for an address that has an account, so that a sign-up form does not say
    /// which addresses do.
    /// </summary>
    public async Task SignUpAsync(string address, string password, CancellationToken cancellationToken)
    {
        using var answer = await _http.PostAsJsonAsync(new Uri(Url, "signup"), new { email = address, password }, cancellationToken);
        if (answer.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Auth answered the sign-up with {(int)answer.StatusCode}: {await answer.Content.ReadAsStringAsync(cancellationToken)}");
        }
    }

    /// <summary>
    /// Signs in with an address and a password. The id of the account that got in, or <see langword="null"/>
    /// when Auth lets nobody in with them.
    /// </summary>
    public async Task<Guid?> SignInAsync(string address, string password, CancellationToken cancellationToken)
        => await AccessTokenAsync(address, password, cancellationToken) is { } token ? SubjectOf(token) : null;

    /// <summary>
    /// Signs in with an address and a password. The access token Auth signed for the account that got in, or
    /// <see langword="null"/> when Auth lets nobody in with them.
    /// </summary>
    public async Task<string?> AccessTokenAsync(string address, string password, CancellationToken cancellationToken)
    {
        using var answer = await _http.PostAsJsonAsync(new Uri(Url, "token?grant_type=password"), new { email = address, password }, cancellationToken);
        if (answer.StatusCode == HttpStatusCode.BadRequest)
        {
            return null;
        }

        if (answer.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Auth answered the sign-in with {(int)answer.StatusCode}: {await answer.Content.ReadAsStringAsync(cancellationToken)}");
        }

        var session = await answer.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return session.GetProperty("access_token").GetString()!;
    }

    /// <summary>
    /// The person opens the link of <paramref name="mail"/>, which proves the address is theirs. Answers the
    /// id of the account Auth signs them into.
    /// </summary>
    public async Task<Guid> FollowLinkAsync(CaughtMail mail, CancellationToken cancellationToken)
    {
        // The link names the project's URL, where a gateway would pass it on to Auth. Here the test is the gateway.
        var link = Regex.Match(mail.Text, Regex.Escape(_linkPrefix) + @"/(?<rest>verify\?[^\s)]+)");
        if (!link.Success)
        {
            throw new InvalidOperationException("The mail has no link to follow: " + mail.Text);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Url, WebUtility.HtmlDecode(link.Groups["rest"].Value)));
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
        using var browser = new HttpClient(handler);
        using var answer = await browser.SendAsync(request, cancellationToken);

        // Auth sends the browser on to the application's page, with the session in the fragment.
        var session = Regex.Match(answer.Headers.Location?.OriginalString ?? "", "[#&]access_token=(?<token>[^&]+)");
        return answer.StatusCode == HttpStatusCode.SeeOther && session.Success
            ? SubjectOf(session.Groups["token"].Value)
            : throw new InvalidOperationException($"Auth answered the link with {(int)answer.StatusCode} and no session: {answer.Headers.Location}");
    }

    private static Guid SubjectOf(string accessToken) => Guid.Parse(new JsonWebToken(accessToken).Subject);
}

/// <summary>The mails the stack's Auth server sent, as its mail catcher kept them.</summary>
public sealed class MailCatcher
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly Uri _url;

    internal MailCatcher(HttpClient http, Uri url)
    {
        _http = http;
        _url = url;
    }

    /// <summary>
    /// The mails sent to <paramref name="address"/>, the latest first, once there are at least
    /// <paramref name="atLeast"/> of them, or the ones that arrived when waiting any longer made no sense.
    /// Auth mails before it answers, so with <paramref name="atLeast"/> at zero this is what was sent.
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
        var found = await _http.GetFromJsonAsync<JsonElement>(new Uri(_url, "api/v1/search?query=" + Uri.EscapeDataString("to:" + address)), cancellationToken);

        var mails = new List<CaughtMail>();
        foreach (var summary in found.GetProperty("messages").EnumerateArray())
        {
            // The search matches loosely; the mail is this address's only when it is among those it was sent to.
            if (!summary.GetProperty("To").EnumerateArray().Any(to => string.Equals(to.GetProperty("Address").GetString(), address, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var mail = await _http.GetFromJsonAsync<JsonElement>(new Uri(_url, "api/v1/message/" + summary.GetProperty("ID").GetString()), cancellationToken);
            mails.Add(new CaughtMail(mail.GetProperty("Subject").GetString() ?? "", mail.GetProperty("Text").GetString() ?? ""));
        }

        return mails;
    }
}

/// <summary>A mail the catcher kept.</summary>
/// <param name="Subject">Its subject.</param>
/// <param name="Text">Its body, as plain text.</param>
public sealed record CaughtMail(string Subject, string Text);
