using DDDToolkit.Startup;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// The sample's host, run in the test's process: composed, checked and started exactly as <c>dotnet run</c> does
/// it, before the first call is answered. Where its database is, the fixture that makes it says.
/// </summary>
/// <remarks>
/// No test makes one. A host comes from one of three places, each of which knows what database it gives it:
/// <see cref="SampleOnPostgres"/>, on a database of the test's own on Supabase's Postgres image, which
/// <see cref="SampleHosts"/> hands to a test class; <see cref="SupabaseCliStack"/>, on the stack the Supabase
/// CLI started; and <see cref="WithoutDatabase"/>, for a test that reads what the host is and stores nothing.
/// <see cref="WithoutAConnectionString"/> makes the one host that must not start.
/// <para>
/// The environment and the settings are the caller's. Settings are passed the way command-line arguments are,
/// so they win over the settings files and <c>Program</c> sees them before it builds the host. In Development
/// <c>appsettings.Development.json</c> applies: the local Supabase URL and secret, the dev login, and the
/// demonstration data.
/// </para>
/// <para>
/// A 500 tells its client nothing, by design, so a test that met one would have nothing to go on. The host's
/// errors are therefore kept (<see cref="Errors"/>), and a client made here writes them to the output of the test
/// whose request was answered with a 500: the exception, with its stack.
/// </para>
/// </remarks>
public sealed class SampleFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// A connection string for the role the host logs in as, to a port of this machine nothing listens on: the
    /// host is on Postgres as far as its registrations go, and a connection anything asks for fails.
    /// </summary>
    private const string NobodyAnswers = "Host=127.0.0.1;Port=1;Username=" + SampleOnPostgres.LoginRole;

    private readonly string _environment;
    private readonly IReadOnlyDictionary<string, string> _settings;
    private readonly Action<IServiceCollection>? _services;

    /// <summary>What the host logged as an error, each with what was thrown: the reason of a 500, or of a start that failed.</summary>
    public HostErrors Errors { get; } = new();

    // Private, so that it is no class fixture and no test news one up: a host is made where its database is known.
    private SampleFactory(string environment, IReadOnlyDictionary<string, string> settings, Action<IServiceCollection>? services)
    {
        _environment = environment;
        _settings = settings;
        _services = services;
    }

    /// <summary>
    /// The host in <paramref name="environment"/>, with <paramref name="settings"/> over its own: for the
    /// fixtures, which put the host's connection string among them. A test asks its fixture.
    /// </summary>
    /// <param name="environment">The environment it runs in.</param>
    /// <param name="settings">Settings that win over the settings files; an empty value clears one.</param>
    /// <param name="services">Changes to its services, made after the host's own registrations.</param>
    public static SampleFactory In(string environment, IReadOnlyDictionary<string, string> settings, Action<IServiceCollection>? services = null)
        => new(environment, settings, services);

    /// <summary>
    /// The host as it is composed on Postgres, with no database behind it: everything it registers, its routes
    /// and its GraphQL schemas, for a test that reads what the host is and never what it stores. It needs no
    /// Docker, so such a test runs in every build.
    /// </summary>
    /// <remarks>
    /// The host is given a connection string nothing answers, and what would use it at start-up is taken out or
    /// turned off: every hosted service of the sample, of the toolkit's Entity Framework package and of the supporting
    /// domains, which are the seeding, the outbox pollers and Tenancy's sync of the role packs, are taken out, and the
    /// start-up checks are turned off, with the reason, the way a host does without them. ASP.NET Core's own hosted services stay, and so does the in-memory gateway's
    /// check that the modules' schemas compose. Nothing else of the host is changed, so a request that needs a row
    /// fails here, which is what such a test should never send.
    /// </remarks>
    /// <param name="services">
    /// Changes to its services, made after the host's own registrations and before the hosted services are taken
    /// out: what it sees is everything the host registers.
    /// </param>
    /// <param name="environment">
    /// The environment it runs in; Development when left out. Outside Development the host's settings files hold
    /// next to nothing, so what it cannot start without comes in <paramref name="settings"/>.
    /// </param>
    /// <param name="settings">Settings over its own, besides the connection string and the seeding, which are these.</param>
    public static SampleFactory WithoutDatabase(
        Action<IServiceCollection>? services = null,
        string? environment = null,
        IReadOnlyDictionary<string, string>? settings = null)
        => new(
            environment ?? Environments.Development,
            new Dictionary<string, string>(settings ?? new Dictionary<string, string>())
            {
                ["ConnectionStrings:" + SampleStorage.ConnectionString] = NobodyAnswers,
                [DemoSeeder.Setting] = "false",
            },
            registered =>
            {
                services?.Invoke(registered);
                foreach (var hosted in registered.Where(NeedsTheDatabaseAtStartUp).ToList())
                {
                    registered.Remove(hosted);
                }

                registered.SkipStartupChecks(WithoutADatabase);
            });

    /// <summary>Why the host <see cref="WithoutDatabase"/> makes runs none of its start-up checks.</summary>
    public const string WithoutADatabase = "the host is composed for tests that read what it registers, and nothing answers at the database it is given";

    /// <summary>
    /// The host as <c>dotnet run</c> in its folder starts it, with nothing configured: in Development, and with no
    /// connection string, whatever the machine's environment says of one. It has no database to run on, so it
    /// must not start; the test that makes it reads why (<see cref="RefusedStarts.RefusedStart"/>).
    /// </summary>
    public static SampleFactory WithoutAConnectionString()
        => new(Environments.Development, new Dictionary<string, string> { ["ConnectionStrings:" + SampleStorage.ConnectionString] = string.Empty }, services: null);

    /// <summary>
    /// Whether a registration is a hosted service of the sample, of the toolkit's Entity Framework package or of a
    /// supporting domain: those are the ones that ask the database something when the host starts, or keep asking
    /// while it runs, Tenancy's sync of the role packs once the host has started among them.
    /// </summary>
    private static bool NeedsTheDatabaseAtStartUp(ServiceDescriptor descriptor)
    {
        if (descriptor.IsKeyedService || descriptor.ServiceType != typeof(IHostedService))
        {
            return false;
        }

        // A hosted service is registered by its type, as an instance, or by a factory whose own type says what it makes.
        var implementation = descriptor.ImplementationType
            ?? descriptor.ImplementationInstance?.GetType()
            ?? descriptor.ImplementationFactory!.GetType().GenericTypeArguments[^1];
        var assembly = implementation.Assembly.GetName().Name!;

        return assembly.StartsWith("Examples.Tenancy.", StringComparison.Ordinal)
            || assembly.StartsWith("DDDToolkit.EntityFramework", StringComparison.Ordinal)
            || assembly.StartsWith("DDDToolkit.Supporting.", StringComparison.Ordinal);
    }

    /// <summary>
    /// A client of the host, as <see cref="WebApplicationFactory{TEntryPoint}.CreateClient()"/> makes one, that
    /// also shows what the host threw: a 500 writes the host's errors to the output of the test that was answered it.
    /// </summary>
    public new HttpClient CreateClient()
        => CreateDefaultClient(new RedirectHandler(), new CookieContainerHandler(), new ShowsWhatTheHostThrew(Errors));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services => services.AddSingleton<ILoggerProvider>(Errors));
        if (_services is not null)
        {
            builder.ConfigureTestServices(_services);
        }
    }

    /// <summary>
    /// Writes what the host logged while a request was answered to the output of the test that sent it, when the
    /// answer is a 500. It runs in the test's own flow, on the way back, which is where that output is.
    /// </summary>
    private sealed class ShowsWhatTheHostThrew(HostErrors errors) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var before = errors.Count;
            var response = await base.SendAsync(request, cancellationToken);

            if ((int)response.StatusCode >= 500)
            {
                // The host answers first and logs what it could not handle after: the answer is here a moment
                // before the reason, which is waited for, briefly.
                for (var waited = 0; errors.Count == before && waited < 100; waited++)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(20), CancellationToken.None);
                }

                TestContext.Current.TestOutputHelper?.WriteLine(
                    $"{request.Method} {request.RequestUri?.PathAndQuery} was answered {(int)response.StatusCode}. The host logged:{Environment.NewLine}{errors.Describe(before)}");
            }

            return response;
        }
    }
}
