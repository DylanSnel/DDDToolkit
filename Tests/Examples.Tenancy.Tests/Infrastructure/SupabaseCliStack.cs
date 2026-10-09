using DDDToolkit.EntityFramework.Tests.Infrastructure;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// The stack <c>supabase start</c> made from <c>Examples/Tenancy</c>, and the sample's host on it, set up by the
/// steps the README gives: the login role gets a password, and the host gets that role's connection string, the
/// key Auth's admin API takes and a password for the demonstration people.
/// </summary>
/// <remarks>
/// Where the stack answers is what <c>supabase/config.toml</c> fixes and the CLI gives every local stack: the
/// database on 54322 with <c>postgres</c> as the role that owns it, and the gateway on 54321 with Auth behind
/// it.
/// <para>
/// The tests work in the stack's own database, and change two passwords there. So they run only where
/// <see cref="RequiredVariable"/> is set: a developer who has the stack up for a host of their own, and runs every
/// test of the project, finds the stack as they left it.
/// </para>
/// Three settings are read from the environment, under the names the host reads them by, so a developer's
/// own are used when they are set:
/// <list type="bullet">
/// <item><c>ConnectionStrings__Supabase</c>, the login role's connection string. Without it the login role is
/// given a password of this run, which is the README's step, and a host started earlier with another password
/// has to be given it again.</item>
/// <item><c>Supabase__SecretKey</c>, the secret key <c>supabase status</c> shows. Without it a token with the
/// service role is signed with the local stack's secret, which the CLI's Auth takes as well.</item>
/// <item><c>Sample__DemoPassword</c>, the password the demonstration people sign in with. Without it, one of this run.</item>
/// </list>
/// </remarks>
public sealed class SupabaseCliStack : IAsyncLifetime
{
    /// <summary>
    /// The environment variable that says these tests are meant, and may work in the stack: set where the workflow
    /// started the stack, and by a developer who runs them on purpose. With it a stack that is not there is a
    /// failure; without it nothing is asked of any stack, and the tests are skipped.
    /// </summary>
    public const string RequiredVariable = "DDDTOOLKIT_REQUIRE_SUPABASE_CLI";

    /// <summary>The database, as the role that owns it: what <c>supabase status</c> shows as the database's URL.</summary>
    public const string AsOwner = "Host=127.0.0.1;Port=54322;Database=postgres;Username=postgres;Password=postgres;Pooling=false;Timeout=5;Include Error Detail=true";

    /// <summary>Where the CLI's Auth answers, behind its gateway.</summary>
    public static readonly Uri Auth = new(SampleSupabaseStack.ProjectUrl + "/auth/v1/");

    private readonly SemaphoreSlim _starting = new(1, 1);
    private SampleFactory? _host;
    private bool _started;
    private string? _whyNot;

    /// <summary>A connection string to the database for the role the host logs in as, without a pool.</summary>
    public string AsLoginRole { get; private set; } = "";

    /// <summary>The password the demonstration people sign in with at the CLI's Auth.</summary>
    public string DemoPassword { get; } = Environment.GetEnvironmentVariable("Sample__DemoPassword") is { Length: >= DemoAuthUsers.MinimumPasswordLength } given
        ? given
        : "Pw-" + Guid.NewGuid().ToString("N");

    public async ValueTask InitializeAsync()
    {
        var cancellation = TestContext.Current.CancellationToken;

        // Not asked for: whatever answers on the stack's ports is somebody's, and is left alone.
        if (!Asked)
        {
            _whyNot = $"these tests change the login role's password and the demonstration people's in the stack they find, so they run only when {RequiredVariable}=1 says they are meant";
            return;
        }

        // Is the stack there, and is it this sample's? Another project's local stack answers on the same ports.
        try
        {
            await using var owner = new NpgsqlConnection(AsOwner);
            await owner.OpenAsync(cancellation);
            await using var asked = new NpgsqlCommand("SELECT to_regclass('supabase_migrations.schema_migrations') IS NOT NULL AND to_regnamespace('tenancy') IS NOT NULL", owner);
            if (!(bool)(await asked.ExecuteScalarAsync(cancellation))!)
            {
                _whyNot = "the database on 127.0.0.1:54322 is not one the Supabase CLI made from Examples/Tenancy";
                return;
            }
        }
        catch (Exception exception) when (exception is NpgsqlException or System.Net.Sockets.SocketException or TimeoutException)
        {
            _whyNot = "nothing answers as the stack's database on 127.0.0.1:54322 (" + exception.Message.Trim() + ")";
            return;
        }

        // The README's one step as the database's owner, unless the developer did it and says which role it is.
        if (Environment.GetEnvironmentVariable("ConnectionStrings__Supabase") is { Length: > 0 } theirs)
        {
            AsLoginRole = new NpgsqlConnectionStringBuilder(theirs) { Pooling = false }.ConnectionString;
        }
        else
        {
            var password = Guid.NewGuid().ToString("N");
            await SampleSupabaseStack.ExecuteAsync(AsOwner, $"ALTER ROLE {SampleOnPostgres.LoginRole} WITH LOGIN PASSWORD '{password}'", cancellation);
            AsLoginRole = new NpgsqlConnectionStringBuilder(AsOwner) { Username = SampleOnPostgres.LoginRole, Password = password }.ConnectionString;
        }

        // The host as the README starts it. Where Auth answers and what its tokens are checked with come from the
        // host's development settings: the project's URL is the CLI's gateway.
        _host = SampleFactory.In(Environments.Development, new Dictionary<string, string>
        {
            ["ConnectionStrings:" + SampleStorage.ConnectionString] = new NpgsqlConnectionStringBuilder(AsLoginRole) { Pooling = true }.ConnectionString,
            [DemoAuthUsers.SecretKeySetting] = Environment.GetEnvironmentVariable("Supabase__SecretKey") is { Length: > 0 } key ? key : SampleSupabaseStack.ServiceRoleKey(),
            [DemoAuthUsers.Setting] = "true",
            [DemoAuthUsers.PasswordSetting] = DemoPassword,
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }

        _starting.Dispose();
    }

    /// <summary>Whether the run asks for these tests: <see cref="RequiredVariable"/> is set.</summary>
    private static bool Asked => RequiredContainers.IsSet(Environment.GetEnvironmentVariable(RequiredVariable));

    /// <summary>Skips the test where the run does not ask for it, and fails it where it does and the stack is not running.</summary>
    public void EnforceOrSkip()
    {
        if (_whyNot is null)
        {
            return;
        }

        if (!Asked)
        {
            Assert.Skip($"The tests on the stack the Supabase CLI starts for the Tenancy sample were not asked for: {_whyNot}.");
        }

        Assert.Fail(
            $"The stack the Supabase CLI starts for the Tenancy sample is not there: {_whyNot}. Start it with 'supabase start' in Examples/Tenancy; these tests start no container themselves. " +
            $"{RequiredVariable} is set, so this test failed rather than skipping: a run that skipped here would have reported green without touching the stack.");
    }

    /// <summary>
    /// The host, started and seeded, with the database's clock past the moment the seeding ended: the host stamps
    /// what it seeds with its own clock and the policies ask the database's.
    /// </summary>
    public async Task<SampleFactory> StartedAsync()
    {
        EnforceOrSkip();
        var cancellation = TestContext.Current.CancellationToken;

        await _starting.WaitAsync(cancellation);
        try
        {
            if (!_started)
            {
                _ = _host!.Server;
                var seeded = DateTimeOffset.UtcNow;

                await using var owner = new NpgsqlConnection(AsOwner);
                await owner.OpenAsync(cancellation);
                await using var clock = new NpgsqlCommand("SELECT now()", owner);
                for (var attempt = 0; new DateTimeOffset(((DateTime)(await clock.ExecuteScalarAsync(cancellation))!).ToUniversalTime(), TimeSpan.Zero) <= seeded; attempt++)
                {
                    if (attempt == 300)
                    {
                        throw new TimeoutException($"The database's clock is still before {seeded:O} after thirty seconds: the container's clock is too far behind this machine's.");
                    }

                    await Task.Delay(100, cancellation);
                }

                _started = true;
            }
        }
        finally
        {
            _starting.Release();
        }

        return _host!;
    }
}
