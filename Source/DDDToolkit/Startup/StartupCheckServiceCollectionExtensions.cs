using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DDDToolkit.Startup;

/// <summary>
/// Registration of the checks a host runs before it serves anything. A package registers its checks where it
/// registers what they are about; the host runs every one of them with one call, and turns one off, or all, with
/// the reason in its code.
/// <code>
/// builder.Services.AddSupabaseRowLevelSecurity();     // brings the checks of the role the host logs in as
/// builder.Services.AddTenancyPostgres();              // brings Tenancy's checks of the database
/// builder.Services.RunStartupChecks();                // runs them all before the server binds its port
/// </code>
/// </summary>
public static class StartupCheckServiceCollectionExtensions
{
    /// <summary>
    /// Registers <paramref name="check"/>, which runs once the host asks for its checks
    /// (<see cref="RunStartupChecks"/>), and not in a host that never does. A package calls it from the
    /// registration that brings what the check is about, so a host that uses the package gets the check without
    /// naming it. Registering a check under a name already taken registers nothing, so a registration a host calls
    /// more than once brings its checks once.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="check">The check.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="check"/> is null.</exception>
    public static IServiceCollection AddStartupCheck(this IServiceCollection services, StartupCheck check)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(check);

        ChecksOf(services).Add(check);
        return services;
    }

    /// <summary>
    /// Runs every start-up check the host's registrations brought, the ones registered after this call included,
    /// before the host starts anything: in their stages (<see cref="StartupCheckStage"/>), as the application itself
    /// (<c>Caller.System</c>), and before the server binds its port. The first check that fails stops the start
    /// with what it threw, unchanged, so the message is the check's own; the log names the check as well.
    /// <para>
    /// They run in <see cref="IHostedLifecycleService.StartingAsync"/>, which the host calls for every lifecycle
    /// service before it calls any hosted service's <see cref="IHostedService.StartAsync"/>, the web server's
    /// included, whatever order they were registered in. So nothing of the host has started when they run: no
    /// port is bound, no queue is read, no seeding has begun.
    /// </para>
    /// <para>
    /// Among the lifecycle services, the runner sits where this call is, and a second call moves it there again. The
    /// host calls their <c>StartingAsync</c> in the order they were registered, so something a host must do to its
    /// database before it is checked, it does before <c>app.Run()</c>, or in the <c>StartingAsync</c> of a lifecycle
    /// service of its own registered before this call. A host that starts its services concurrently
    /// (<c>HostOptions.ServicesStartConcurrently</c>) calls them all at once, so there it does that before
    /// <c>app.Run()</c>.
    /// </para>
    /// <para>
    /// A host that still calls some of the checks by hand, from a hosted service of its own, runs those twice:
    /// once here and once there, after. Each of the toolkit's checks reads and changes nothing, so the second run
    /// costs a few queries at start-up and nothing else.
    /// </para>
    /// <para>
    /// Without this call no check runs: a check opens connections and can refuse the start, so whether a host runs
    /// them is a line in its own code, not something a registration does out of sight. Calling it more than once
    /// is harmless.
    /// </para>
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection RunStartupChecks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        ChecksOf(services).RunAll();

        // Where the host asked, the last time it did: a runner left where an earlier call put it would run the checks
        // before a lifecycle service the host registered in between, its own migration say, and refuse the start.
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (IsRunner(services[i]))
            {
                services.RemoveAt(i);
            }
        }

        AddRunner(services);
        return services;
    }

    /// <summary>
    /// Turns the start-up check named <paramref name="name"/> off, for <paramref name="reason"/>, which the log
    /// repeats when the host starts. For a check whose question the host answers otherwise, say a deployment that
    /// checks its migrations itself before it starts the host. A name no registration brought is logged as a
    /// warning when the host starts, since a name spelled wrong turns nothing off. Turning a check off a second
    /// time replaces the reason.
    /// <code>
    /// builder.Services.SkipStartupCheck(SupabaseMigrations.AppliedCheck, reason: "the deployment checks the migrations before it starts the host");
    /// </code>
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="name">The check's name, as its package documents it and the log writes it.</param>
    /// <param name="reason">Why the host does without it: for whoever reads the code or the log next.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="reason"/> is empty.</exception>
    public static IServiceCollection SkipStartupCheck(this IServiceCollection services, string name, string reason)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        ChecksOf(services).Skip(name, reason);
        return services;
    }

    /// <summary>
    /// Turns every start-up check off, for <paramref name="reason"/>, which the log repeats when the host starts:
    /// for a host composed for something other than serving, a test that reads what the host registers with no
    /// database behind it, say.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="reason">Why the host does without them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is empty.</exception>
    public static IServiceCollection SkipStartupChecks(this IServiceCollection services, string reason)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        ChecksOf(services).SkipAll(reason);
        return services;
    }

    /// <summary>
    /// The host's start-up checks, as registered so far: the one instance in <paramref name="services"/>, added the
    /// first time anything asks. Read before the host is built, so a test can see what a host would run.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static StartupChecks GetStartupChecks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return ChecksOf(services);
    }

    /// <summary>The one instance the services hold, found among the registrations, or registered now.</summary>
    private static StartupChecks ChecksOf(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(StartupChecks) && !services[i].IsKeyedService && services[i].ImplementationInstance is StartupChecks registered)
            {
                return registered;
            }
        }

        var checks = new StartupChecks();
        services.AddSingleton(checks);
        return checks;
    }

    /// <summary>The runner, once however many times it is asked for.</summary>
    private static void AddRunner(IServiceCollection services)
        => services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, StartupCheckRunner>());

    /// <summary>Whether <paramref name="descriptor"/> registers the runner.</summary>
    private static bool IsRunner(ServiceDescriptor descriptor)
        => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(StartupCheckRunner);
}
