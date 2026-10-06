using System.Collections.Concurrent;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework;
using DDDToolkit.Startup;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// <c>AddTenancy</c> brings Tenancy's start-up checks with it: the catalogue builds, every context that keeps rows
/// to a tenant checks its saves, and a key a role holds that the catalogue has lost is logged. A host that runs its
/// checks gets them without a class of its own; each is the method it always was, run as the application.
/// </summary>
public sealed class StartupCheckTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void AddTenancy_brings_the_checks_of_the_catalogue_the_contexts_and_the_stored_keys()
    {
        var services = new ServiceCollection();
        TestHostTenancy.Add(services);
        TestHostTenancy.Add(services);

        services.GetStartupChecks().InOrder().Select(check => (check.Name, check.Stage)).Should().Equal(
            (TenancyChecks.CatalogueBuildsCheck, StartupCheckStage.Services),
            (TenancyChecks.ContextsWiredCheck, StartupCheckStage.Services),
            (TenancyChecks.UnknownStoredKeysCheck, StartupCheckStage.Database));
    }

    [Fact]
    public async Task Every_check_the_registrations_brought_passes_on_services_wired_as_documented()
    {
        using var services = new TestServices();

        var ran = await RunAllAsync(services.Provider);

        ran.Should().Equal(
            EntityFrameworkChecks.ToolkitWiredCheck,
            TenancyChecks.CatalogueBuildsCheck,
            TenancyChecks.ContextsWiredCheck,
            TenancyChecks.UnknownStoredKeysCheck);
    }

    [Fact]
    public async Task A_context_that_does_not_check_its_saves_is_refused_by_the_check_of_the_contexts()
    {
        using var services = new TestServices(Wiring.WithoutTenancy);

        var run = () => RunAllAsync(services.Provider);

        // The toolkit's check of the contexts, which runs first, finds the part the model requires missing, and
        // names the call that puts it on; Tenancy's own check of the contexts would refuse it as well.
        (await run.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*cannot do without the part tenancy.save-check*no TenancySaveInterceptor*options.UseTenancy(serviceProvider).");
        FluentActions.Invoking(() =>
        {
            using var scope = services.Provider.CreateScope();
            TenancyChecks.EnsureWired(scope.ServiceProvider.GetRequiredService<TestWidgetContext>());
        }).Should().Throw<InvalidOperationException>().WithMessage("*keeps entities to a tenant but has no TenancySaveInterceptor*");
    }

    [Fact]
    public async Task A_key_a_role_holds_that_the_catalogue_has_lost_is_logged_and_refuses_nothing()
    {
        // The application once declared a key and gave it to a role; a later version removed it from the code.
        const string polish = "widget.polish";
        using var before = new TestServices(configure: registered => registered.AddTenancyPermissions([new Permission(polish, "Widgets", "Polish widgets")]));
        var harbor = await before.ProvisionAsync("harbor");
        await before.BySystemIn(harbor.Tenant, services =>
            services.Roles().CreateAsync("Polisher", "Polishes widgets", [polish, HostCatalogue.WidgetRead], Cancellation));

        var logs = new ConcurrentQueue<(LogLevel Level, string Message)>();
        using var after = new TestServices(database: before.Database, configure: registered => registered.AddLogging(logging => logging.AddProvider(new Recorded(logs))));

        await RunAllAsync(after.Provider);

        logs.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message).Should().Equal(
            "A role holds the key widget.polish, which the catalogue does not know. It gives no rights. Retire a key rather than removing it from the code, or take it off the roles that hold it.");
    }

    /// <summary>
    /// Runs every start-up check the services' registrations brought, in their order and as the application, as
    /// the host's runner does, and answers their names.
    /// </summary>
    private static async Task<IReadOnlyList<string>> RunAllAsync(IServiceProvider services)
    {
        var ran = new List<string>();
        using (Callers.Begin(Caller.System))
        {
            foreach (var check in services.GetRequiredService<StartupChecks>().InOrder())
            {
                await check.RunAsync(services, Cancellation);
                ran.Add(check.Name);
            }
        }

        return ran;
    }

    /// <summary>Keeps every line any logger writes.</summary>
    private sealed class Recorded(ConcurrentQueue<(LogLevel Level, string Message)> lines) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Recorder(lines);

        public void Dispose()
        {
        }

        private sealed class Recorder(ConcurrentQueue<(LogLevel Level, string Message)> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    lines.Enqueue((logLevel, formatter(state, exception)));
                }
            }
        }
    }
}
