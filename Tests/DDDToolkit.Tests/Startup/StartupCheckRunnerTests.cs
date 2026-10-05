using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Startup;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDDToolkit.Tests.Startup;

/// <summary>
/// The one runner of a host's start-up checks: it runs every check the registrations brought, by stage and then as
/// registered, as the application itself, before anything of the host starts and before the server binds its port;
/// the first check that fails stops the start with what it threw; and a host turns one check off by name, or all of
/// them, with its reason, which the log repeats.
/// </summary>
public sealed class StartupCheckRunnerTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Checks_run_by_stage_then_as_registered_with_a_check_moved_in_front_of_the_ones_it_names()
    {
        var ran = new ConcurrentQueue<string>();
        using var host = Build(services => services
            .RunStartupChecks()
            .AddStartupCheck(Recorded("tests.database-first", StartupCheckStage.Database, ran))
            .AddStartupCheck(Recorded("tests.services-first", StartupCheckStage.Services, ran))
            .AddStartupCheck(Recorded("tests.migrations", StartupCheckStage.Migrations, ran))
            .AddStartupCheck(Recorded("tests.database-second", StartupCheckStage.Database, ran, runsBefore: ["tests.database-first"]))
            .AddStartupCheck(Recorded("tests.login", StartupCheckStage.Login, ran))
            .AddStartupCheck(Recorded("tests.services-second", StartupCheckStage.Services, ran, runsBefore: ["tests.services-first", "another.package-not-here"]))
            .AddStartupCheck(Recorded("tests.database-third", StartupCheckStage.Database, ran)));

        await host.StartAsync(Cancellation);

        string[] expected =
        [
            "tests.services-second", "tests.services-first",
            "tests.login",
            "tests.migrations",
            "tests.database-second", "tests.database-first", "tests.database-third",
        ];
        ran.Should().Equal(expected, "every check of a stage runs before any of the next, as registered, unless it says it runs before another");
        host.Services.GetRequiredService<StartupChecks>().InOrder().Select(check => check.Name)
            .Should().Equal(expected, "the order a test reads is the order the runner takes");

        await host.StopAsync(Cancellation);
    }

    [Fact]
    public async Task Each_check_runs_as_the_application_itself_whatever_a_check_before_it_left_behind()
    {
        // A check that begins a caller of its own and never ends it, without awaiting anything, writes that caller
        // into the flow the runner runs in. The check after it still runs as the application.
        var seen = new ConcurrentQueue<Caller?>();
        using var host = Build(services => services
            .RunStartupChecks()
            .AddStartupCheck(new StartupCheck("tests.leaves-a-caller-behind", StartupCheckStage.Services, (_, _) =>
            {
                seen.Enqueue(Callers.Ambient);
                _ = Callers.Begin(Caller.User(Guid.NewGuid()));
                return Task.CompletedTask;
            }))
            .AddStartupCheck(new StartupCheck("tests.after-it", StartupCheckStage.Database, (_, _) =>
            {
                seen.Enqueue(Callers.Ambient);
                return Task.CompletedTask;
            })));

        await host.StartAsync(Cancellation);

        seen.Should().Equal(
            [Caller.System, Caller.System],
            "a check is the application's own bookkeeping, and a host that requires explicit callers refuses work that names none");

        await host.StopAsync(Cancellation);
    }

    [Fact]
    public async Task A_check_that_fails_stops_the_start_with_what_it_threw_and_no_later_check_runs()
    {
        var ran = new ConcurrentQueue<string>();
        var found = new InvalidOperationException("The ledger has no table for refunds. Fix: apply the migration that makes it.");
        var logs = new RecordedLogs();
        using var host = Build(
            services => services
                .RunStartupChecks()
                .AddStartupCheck(Recorded("tests.wired", StartupCheckStage.Services, ran))
                .AddStartupCheck(new StartupCheck("tests.ledger", StartupCheckStage.Database, (_, _) => throw found))
                .AddStartupCheck(Recorded("tests.after-the-ledger", StartupCheckStage.Database, ran)),
            logs);

        var start = () => host.StartAsync(Cancellation);

        var thrown = (await start.Should().ThrowAsync<InvalidOperationException>()).Which;
        thrown.Should().BeSameAs(found, "the host is told what the check found, in the check's own words and type");
        thrown.Data[StartupChecks.FailedCheckKey].Should().Be("tests.ledger", "the exception carries the name of the check that threw it");
        ran.Should().Equal(["tests.wired"], "the first check that fails stops the start, and nothing after it runs");
        logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Error && entry.Message.Contains("'tests.ledger' refused the start", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_check_turned_off_by_name_does_not_run_and_the_log_says_why()
    {
        var ran = new ConcurrentQueue<string>();
        var logs = new RecordedLogs();
        using var host = Build(
            services => services
                .AddStartupCheck(Recorded("tests.migrations", StartupCheckStage.Migrations, ran))
                .AddStartupCheck(Recorded("tests.policies", StartupCheckStage.Database, ran))
                .SkipStartupCheck("tests.migrations", reason: "the deployment checks the migrations before it starts the host")
                .RunStartupChecks(),
            logs);

        await host.StartAsync(Cancellation);

        ran.Should().Equal(["tests.policies"]);
        logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Information
            && entry.Message == "The start-up check 'tests.migrations' is turned off: the deployment checks the migrations before it starts the host");
        logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Information && entry.Message.StartsWith("The start-up checks passed in ", StringComparison.Ordinal)
            && entry.Message.EndsWith(" ms: tests.policies.", StringComparison.Ordinal));

        await host.StopAsync(Cancellation);
    }

    [Fact]
    public async Task Every_check_is_turned_off_with_one_reason_those_on_by_default_included()
    {
        var ran = new ConcurrentQueue<string>();
        var logs = new RecordedLogs();
        using var host = Build(
            services => services
                .RunStartupChecks()
                .AddStartupCheck(Recorded("tests.policies", StartupCheckStage.Database, ran))
                .AddStartupCheck(Recorded("tests.extension", StartupCheckStage.Database, ran, onByDefault: true))
                .SkipStartupChecks(reason: "the host is composed to read its registrations, with no database behind it"),
            logs);

        await host.StartAsync(Cancellation);

        ran.Should().BeEmpty();
        logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Information
            && entry.Message == "Every start-up check is turned off: the host is composed to read its registrations, with no database behind it");

        await host.StopAsync(Cancellation);
    }

    [Fact]
    public async Task A_host_that_does_not_ask_for_its_checks_runs_only_those_on_by_default()
    {
        // What an application that upgrades has: its registrations bring checks it never ran, and they stay unrun.
        var ran = new ConcurrentQueue<string>();
        using var host = Build(services => services
            .AddStartupCheck(Recorded("tests.policies", StartupCheckStage.Database, ran))
            .AddStartupCheck(Recorded("tests.extension", StartupCheckStage.Database, ran, onByDefault: true)));

        await host.StartAsync(Cancellation);

        ran.Should().Equal(["tests.extension"], "a check its package ran by itself before keeps running; the rest wait for the host to ask");
        await host.StopAsync(Cancellation);

        // With no check on by default, nothing of the runner is there at all.
        var services = new ServiceCollection().AddStartupCheck(Recorded("tests.policies", StartupCheckStage.Database, ran));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IHostedService), "a host that does not ask gets the start-up it had");
    }

    [Fact]
    public async Task A_host_that_does_not_ask_starts_whatever_the_checks_it_did_not_ask_for_say_of_one_another()
    {
        // Two checks the host never asked for contradict each other; it starts as it did, with the one on by default.
        var ran = new ConcurrentQueue<string>();
        using var host = Build(services => services
            .AddStartupCheck(Recorded("tests.wired", StartupCheckStage.Services, ran))
            .AddStartupCheck(Recorded("tests.policies", StartupCheckStage.Database, ran, runsBefore: ["tests.wired"]))
            .AddStartupCheck(Recorded("tests.extension", StartupCheckStage.Database, ran, onByDefault: true)));

        await host.StartAsync(Cancellation);

        ran.Should().Equal(["tests.extension"]);
        await host.StopAsync(Cancellation);
    }

    [Fact]
    public async Task Checks_run_before_the_server_binds_its_port_and_before_any_hosted_service_starts()
    {
        var port = FreePort();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Logging.ClearProviders();

        // A hosted service registered before the runner, and the server, which the host starts after both.
        builder.Services.AddSingleton<StartedServices>();
        builder.Services.AddHostedService<RecordsItsStart>();

        bool? answered = null;
        IReadOnlyList<string>? startedBefore = null;
        builder.Services.AddStartupCheck(new StartupCheck("tests.port-still-closed", StartupCheckStage.Services, async (services, cancellationToken) =>
        {
            answered = await AnswersAsync(port, cancellationToken);
            startedBefore = [.. services.GetRequiredService<StartedServices>().Names];
        }));
        builder.Services.RunStartupChecks();

        await using var app = builder.Build();
        app.MapGet("/", () => "up");
        await app.StartAsync(Cancellation);

        answered.Should().BeFalse("nothing listened on the port while the checks ran");
        startedBefore.Should().BeEmpty("no hosted service had started either, the one registered before the runner included");
        (await AnswersAsync(port, Cancellation)).Should().BeTrue("the server binds its port once the checks have passed");

        await app.StopAsync(Cancellation);
    }

    [Fact]
    public async Task Checks_run_before_the_server_binds_its_port_in_a_host_that_registered_the_server_first()
    {
        // The generic host with a web host registers the server's hosted service where ConfigureWebHostDefaults is
        // called: before the runner here. The checks run before it all the same.
        var port = FreePort();
        bool? answered = null;
        using var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureWebHostDefaults(web => web
                .UseUrls($"http://127.0.0.1:{port}")
                .Configure(app => app.Run(context => context.Response.WriteAsync("up"))))
            .ConfigureServices(services => services
                .AddStartupCheck(new StartupCheck("tests.port-still-closed", StartupCheckStage.Services, async (_, cancellationToken) => answered = await AnswersAsync(port, cancellationToken)))
                .RunStartupChecks())
            .Build();

        var hosted = host.Services.GetServices<IHostedService>().Select(service => service.GetType().Name).ToList();
        hosted.IndexOf("GenericWebHostService").Should().BeLessThan(hosted.IndexOf("StartupCheckRunner"), "the server's service is registered first here");

        await host.StartAsync(Cancellation);

        answered.Should().BeFalse("the checks run in StartingAsync, which the host calls before any StartAsync, the server's included");
        (await AnswersAsync(port, Cancellation)).Should().BeTrue();

        await host.StopAsync(Cancellation);
    }

    [Fact]
    public async Task A_lifecycle_service_registered_before_the_host_asks_for_its_checks_starts_before_them_though_a_check_on_by_default_came_first()
    {
        // A check on by default registers the runner where it is registered, as a pgmq sink does. The host's own
        // migration, registered after it and before the host asks for its checks, still runs before them: asking
        // puts the runner where the host asked.
        var migrated = false;
        using var host = Build(services => services
            .AddStartupCheck(new StartupCheck("tests.extension", StartupCheckStage.Database, (_, _) => Task.CompletedTask) { OnByDefault = true })
            .AddSingleton<IHostedService>(new Migrates(() => migrated = true))
            .AddStartupCheck(new StartupCheck("tests.migrations-applied", StartupCheckStage.Migrations, (_, _) =>
                migrated ? Task.CompletedTask : throw new InvalidOperationException("The database is missing migrations.")))
            .RunStartupChecks());

        await host.StartAsync(Cancellation);

        host.Services.GetServices<IHostedService>().Select(service => service.GetType().Name)
            .Should().Equal([nameof(Migrates), "StartupCheckRunner"], "one runner, after what the host registered before it asked");

        await host.StopAsync(Cancellation);
    }

    [Fact]
    public async Task Checks_run_before_any_hosted_service_starts_in_a_host_that_starts_its_services_concurrently()
    {
        // Starting services concurrently does away with the order of their StartingAsync, not with every
        // StartingAsync coming before any StartAsync.
        IReadOnlyList<string>? startedBefore = null;
        using var host = Build(services => services
            .Configure<HostOptions>(options => options.ServicesStartConcurrently = true)
            .AddSingleton<StartedServices>()
            .AddHostedService<RecordsItsStart>()
            .AddStartupCheck(new StartupCheck("tests.nothing-started-yet", StartupCheckStage.Services, async (services, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
                startedBefore = [.. services.GetRequiredService<StartedServices>().Names];
            }))
            .RunStartupChecks());

        await host.StartAsync(Cancellation);

        startedBefore.Should().BeEmpty("the host calls every StartingAsync, the runner's included, before any StartAsync");
        host.Services.GetRequiredService<StartedServices>().Names.Should().Equal([nameof(RecordsItsStart)]);

        await host.StopAsync(Cancellation);
    }

    [Fact]
    public async Task A_host_that_still_runs_a_check_by_hand_runs_it_twice_and_starts()
    {
        // A host that kept its own start-up class next to the runner: the check reads and changes nothing, so the
        // second run costs its queries and nothing else.
        var runs = 0;
        Task CountedAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref runs);
            return Task.CompletedTask;
        }

        using var host = Build(services => services
            .RunStartupChecks()
            .AddStartupCheck(new StartupCheck("tests.counted", StartupCheckStage.Database, CountedAsync))
            .AddSingleton<IHostedService>(provider => new RunsByHand(provider, CountedAsync)));

        await host.StartAsync(Cancellation);

        runs.Should().Be(2, "the runner ran it, and the host's own class ran it again after");
        await host.StopAsync(Cancellation);
    }

    [Fact]
    public void One_check_per_name_and_one_runner_however_often_they_are_registered()
    {
        var services = new ServiceCollection()
            .AddStartupCheck(new StartupCheck("tests.migrations", StartupCheckStage.Migrations, (_, _) => Task.CompletedTask))
            .AddStartupCheck(new StartupCheck("tests.migrations", StartupCheckStage.Database, (_, _) => throw new InvalidOperationException("Never run.")))
            .RunStartupChecks()
            .RunStartupChecks();

        services.GetStartupChecks().Registered.Should().ContainSingle().Which.Stage.Should().Be(StartupCheckStage.Migrations, "the first registration of a name is the one kept");
        services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)).Should().ContainSingle("one runner runs every check");
        services.Where(descriptor => descriptor.ServiceType == typeof(StartupChecks)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_check_that_says_it_runs_before_one_of_an_earlier_stage_stops_the_start_before_any_check_runs()
    {
        var ran = new ConcurrentQueue<string>();
        using var host = Build(services => services
            .RunStartupChecks()
            .AddStartupCheck(Recorded("tests.wired", StartupCheckStage.Services, ran))
            .AddStartupCheck(Recorded("tests.policies", StartupCheckStage.Database, ran, runsBefore: ["tests.wired"])));

        var start = () => host.StartAsync(Cancellation);

        (await start.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("The start-up check 'tests.policies' runs in the stage Database and says it runs before 'tests.wired', which runs in the earlier stage Services.*");
        ran.Should().BeEmpty();
    }

    [Fact]
    public void Checks_that_each_run_before_the_next_in_a_circle_cannot_be_ordered()
    {
        var services = new ServiceCollection()
            .AddStartupCheck(new StartupCheck("tests.first", StartupCheckStage.Database, (_, _) => Task.CompletedTask) { RunsBefore = ["tests.second"] })
            .AddStartupCheck(new StartupCheck("tests.second", StartupCheckStage.Database, (_, _) => Task.CompletedTask) { RunsBefore = ["tests.first"] })
            .AddStartupCheck(new StartupCheck("tests.free", StartupCheckStage.Database, (_, _) => Task.CompletedTask));

        FluentActions.Invoking(() => services.GetStartupChecks().InOrder()).Should().Throw<InvalidOperationException>()
            .WithMessage("The start-up checks 'tests.first', 'tests.second' of the stage Database cannot be put in an order*");
    }

    [Fact]
    public async Task A_name_turned_off_that_no_registration_brings_is_logged_as_a_warning()
    {
        var logs = new RecordedLogs();
        using var host = Build(
            services => services
                .RunStartupChecks()
                .AddStartupCheck(new StartupCheck("tests.migrations-applied", StartupCheckStage.Migrations, (_, _) => Task.CompletedTask))
                .SkipStartupCheck("tests.migrations-aplied", reason: "a name spelled wrong turns nothing off"),
            logs);

        await host.StartAsync(Cancellation);

        logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning
            && entry.Message.StartsWith("The start-up check 'tests.migrations-aplied' is turned off, and no registration brings a check of that name", StringComparison.Ordinal)
            && entry.Message.EndsWith("The checks this host has are: tests.migrations-applied.", StringComparison.Ordinal));

        await host.StopAsync(Cancellation);
    }

    [Fact]
    public void A_check_is_named_in_one_word_in_one_of_the_stages_and_turned_off_with_a_reason()
    {
        FluentActions.Invoking(() => new StartupCheck("tests.two words", StartupCheckStage.Database, (_, _) => Task.CompletedTask))
            .Should().Throw<ArgumentException>().WithMessage("*holds no white space*");
        FluentActions.Invoking(() => new StartupCheck("tests.check", (StartupCheckStage)7, (_, _) => Task.CompletedTask))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new ServiceCollection().SkipStartupCheck("tests.check", reason: " "))
            .Should().Throw<ArgumentException>("a check is turned off with a reason, in the code");
        FluentActions.Invoking(() => new ServiceCollection().SkipStartupChecks(reason: ""))
            .Should().Throw<ArgumentException>();
    }

    // ------------------------------------------------------------------ the host under test

    /// <summary>A host with nothing in it but <paramref name="configure"/>, logging into <paramref name="logs"/>.</summary>
    private static IHost Build(Action<IServiceCollection> configure, RecordedLogs? logs = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        if (logs is not null)
        {
            builder.Logging.AddProvider(logs);
        }

        configure(builder.Services);
        return builder.Build();
    }

    /// <summary>A check that writes its name to <paramref name="ran"/> when it runs.</summary>
    private static StartupCheck Recorded(string name, StartupCheckStage stage, ConcurrentQueue<string> ran, string[]? runsBefore = null, bool onByDefault = false)
        => new(name, stage, (_, _) =>
        {
            ran.Enqueue(name);
            return Task.CompletedTask;
        })
        {
            RunsBefore = runsBefore ?? [],
            OnByDefault = onByDefault,
        };

    /// <summary>A port of this machine nothing listens on, a moment ago.</summary>
    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Whether something accepts a connection at <paramref name="port"/> of this machine.</summary>
    private static async Task<bool> AnswersAsync(int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>The hosted services that have started, by name.</summary>
    private sealed class StartedServices
    {
        public ConcurrentQueue<string> Names { get; } = new();
    }

    /// <summary>A hosted service that says when it starts.</summary>
    private sealed class RecordsItsStart(StartedServices started) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            started.Names.Enqueue(nameof(RecordsItsStart));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>A host's own lifecycle service that migrates its database as the host starts, in development say.</summary>
    private sealed class Migrates(Action migrate) : IHostedLifecycleService
    {
        public Task StartingAsync(CancellationToken cancellationToken)
        {
            migrate();
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>A host's start-up class of its own, from before the runner, that calls a check by hand.</summary>
    private sealed class RunsByHand(IServiceProvider services, Func<IServiceProvider, CancellationToken, Task> check) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var system = Callers.Begin(Caller.System);
            await check(services, cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>One line of a log.</summary>
    private sealed record LogLine(LogLevel Level, string Category, string Message);

    /// <summary>Keeps what every logger of the host writes.</summary>
    private sealed class RecordedLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogLine> _entries = new();

        public IReadOnlyList<LogLine> Entries => [.. _entries];

        public ILogger CreateLogger(string categoryName) => new Recorder(categoryName, _entries);

        public void Dispose()
        {
        }

        private sealed class Recorder(string category, ConcurrentQueue<LogLine> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => entries.Enqueue(new LogLine(logLevel, category, formatter(state, exception)));
        }
    }
}
