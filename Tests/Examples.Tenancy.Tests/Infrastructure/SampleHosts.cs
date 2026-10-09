using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// The hosts of one test class: one that its tests that change nothing share, started when the first of them
/// asks, and a host of its own for each test that changes something. Each runs on a database of its own on
/// Supabase's own Postgres image (<see cref="SampleSupabaseStack"/>), which the exported files made, logged in as
/// the role that owns nothing: every answer a test reads went through the exported policies and privileges as
/// well as through the application's own checks.
/// </summary>
/// <remarks>
/// A class takes this as its class fixture, asks it for its hosts and never makes one itself. It needs Docker, so
/// it carries the samples' two traits on the class itself, and the builds that have no Docker leave it to the
/// run that does:
/// <code>
/// [Trait("Category", "Samples")]
/// [Trait("Sample", "Tenancy.Supabase")]
/// public sealed class AreaManagerScenarios(SampleHosts sample) : IClassFixture&lt;SampleHosts&gt;
/// {
///     [Fact]
///     public async Task Rhea_sees_the_projects_of_North()
///     {
///         using var rhea = await sample.ClientAsync("rhea", "harbor");      // the host the class shares
///         ...
///     }
///
///     [Fact]
///     public async Task Rhea_opens_a_project_in_North()
///     {
///         await using var host = await sample.StartAsync();                 // a host of this test's own
///         ...
///     }
/// }
/// </code>
/// <para>
/// A host starts on a copy of the database the demonstration was seeded in once for the run
/// (<see cref="SampleOnPostgres"/>), with seeding off, so a host of a test's own costs a copy and a start, and
/// no test sees what another changed. A host that has to seed the demonstration itself is asked for with
/// <c>seeded: false</c>: one that runs on a clock of its own, since the periods it seeds are measured from that
/// clock's moment, and one whose test is about the seeding.
/// </para>
/// <para>
/// The host stamps what it writes with its own clock and the policies ask the database's, so a host is handed
/// out only once the database's clock has passed the moment it started. No test moves time on to see a period
/// end: the database compares periods with a clock of its own, which stays where it is. Such a test gives the
/// period an end a few seconds ahead and waits for it (<see cref="SampleOnPostgres.WaitUntilItIsPastAsync"/>).
/// </para>
/// <para>
/// A test that also asks the database beside the host, as the role that owns it or as the application's own
/// work in a tenant, or that waits for its clock, asks for the host with its database:
/// <see cref="SharedOnPostgresAsync"/> and <see cref="StartOnPostgresAsync"/>.
/// </para>
/// </remarks>
public sealed class SampleHosts(SampleSupabaseStack stack) : IAsyncDisposable
{
    // Started once, by whichever test asks first, and not on that test's cancellation token: a test that is
    // cancelled while the host starts must not leave the others a host that is half there.
    private readonly Lazy<Task<SampleOnPostgres>> _shared = new(() => Task.Run(() => NewHostAsync(stack, null, null, null, seeded: true, CancellationToken.None)));

    /// <summary>The host the class's tests share, in Development, started on the demonstration: for a test that changes nothing.</summary>
    public async Task<SampleFactory> SharedAsync() => (await SharedOnPostgresAsync()).Host;

    /// <summary>The host the class's tests share, with the database it runs on: for a test that changes nothing and also asks the database.</summary>
    public Task<SampleOnPostgres> SharedOnPostgresAsync() => _shared.Value.WaitAsync(TestContext.Current.CancellationToken);

    /// <summary>A host of the calling test's own, started, on a database nobody else uses. The test disposes it.</summary>
    /// <param name="services">Changes to its services, made after the host's own registrations.</param>
    /// <param name="settings">Settings that win over the host's settings files and over what the fixture sets.</param>
    /// <param name="environment">The environment it runs in; Development when left out.</param>
    /// <param name="seeded">
    /// Whether it starts on the demonstration as the run's one seeding left it, with seeding off.
    /// <see langword="false"/> for a host on a database with nothing in it, which does what its environment and
    /// its settings say: in Development it seeds the demonstration itself, under its own clock.
    /// </param>
    public async Task<SampleFactory> StartAsync(
        Action<IServiceCollection>? services = null,
        IReadOnlyDictionary<string, string>? settings = null,
        string? environment = null,
        bool seeded = true)
        => (await StartOnPostgresAsync(services, settings, environment, seeded)).Host;

    /// <summary>
    /// A host of the calling test's own, as <see cref="StartAsync"/> starts one, with the database it runs on.
    /// The test disposes what it is handed, which stops the host.
    /// </summary>
    /// <param name="services">Changes to its services, made after the host's own registrations.</param>
    /// <param name="settings">Settings that win over the host's settings files and over what the fixture sets.</param>
    /// <param name="environment">The environment it runs in; Development when left out.</param>
    /// <param name="seeded">Whether it starts on the demonstration as the run's one seeding left it, as for <see cref="StartAsync"/>.</param>
    public Task<SampleOnPostgres> StartOnPostgresAsync(
        Action<IServiceCollection>? services = null,
        IReadOnlyDictionary<string, string>? settings = null,
        string? environment = null,
        bool seeded = true)
        => NewHostAsync(stack, services, settings, environment, seeded, TestContext.Current.CancellationToken);

    /// <summary>
    /// A host of the calling test's own that has not started, on a database nobody else uses: for a test about
    /// the start itself, which starts it and reads how that went. A host that must not start is asked for this
    /// way, since what stopped it is in what its start throws and in what it logged as it gave up
    /// (<see cref="RefusedStarts"/>), and <see cref="StartAsync"/> would keep neither. The test disposes it.
    /// </summary>
    /// <param name="services">Changes to its services, made after the host's own registrations.</param>
    /// <param name="settings">Settings that win over the host's settings files and over what the fixture sets.</param>
    /// <param name="environment">The environment it runs in; Development when left out.</param>
    public async Task<SampleFactory> NotStartedAsync(
        Action<IServiceCollection>? services = null,
        IReadOnlyDictionary<string, string>? settings = null,
        string? environment = null)
        => (await SampleOnPostgres.CreateAsync(stack, TestContext.Current.CancellationToken, services: services, settings: settings, environment: environment)).Host;

    /// <summary>A client of the shared host that calls as <paramref name="person"/> in the tenant <paramref name="tenant"/> names.</summary>
    public async Task<HttpClient> ClientAsync(string person, string? tenant)
        => await (await SharedAsync()).ClientAsync(person, tenant);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!_shared.IsValueCreated)
        {
            return;
        }

        SampleOnPostgres shared;
        try
        {
            shared = await _shared.Value;
        }
        catch (Exception)
        {
            // It failed to start, which its tests have reported.
            return;
        }

        await shared.DisposeAsync();
    }

    /// <summary>Makes a host on a database of its own, starts it, and waits for the database's clock to pass that moment.</summary>
    private static async Task<SampleOnPostgres> NewHostAsync(
        SampleSupabaseStack stack,
        Action<IServiceCollection>? services,
        IReadOnlyDictionary<string, string>? settings,
        string? environment,
        bool seeded,
        CancellationToken cancellationToken)
    {
        var sample = await SampleOnPostgres.CreateAsync(stack, cancellationToken, services: services, settings: settings, environment: environment, seeded: seeded);
        try
        {
            _ = sample.Host.Server;
            await sample.WaitUntilTheDatabaseIsPastAsync(DateTimeOffset.UtcNow, cancellationToken);

            // The database stays until the stack stops; the host is all there is to dispose.
            return sample;
        }
        catch
        {
            await sample.DisposeAsync();
            throw;
        }
    }
}
