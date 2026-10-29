using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// <c>services.SyncStarterRoles()</c> runs the starter role sync once per start of the host, in the background, once the
/// host has started; a sync that fails is logged and the host goes on, and a host without a sync to run is told
/// what to register.
/// </summary>
public class SyncStarterRolesTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_sync_runs_once_the_host_has_started_and_once_however_often_it_is_asked_for()
    {
        var sync = new CountingSync();
        var (hosted, lifetime, _) = Host(services => services.AddSingleton<IStarterRoleSync>(sync).SyncStarterRoles().SyncStarterRoles());

        await hosted.StartAsync(Cancellation);
        await Task.Delay(50, Cancellation);
        sync.Runs.Should().Be(0, "the host has not started: its checks and its other hosted services come first");

        lifetime.Started();
        (await sync.Ran.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancellation)).Should().BeTrue();
        await ((BackgroundService)hosted).ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        sync.Runs.Should().Be(1);
    }

    [Fact]
    public async Task A_sync_that_fails_is_logged_and_the_host_goes_on()
    {
        var failing = new CountingSync(new InvalidOperationException("the database is away"));
        var (hosted, lifetime, log) = Host(services => services.AddSingleton<IStarterRoleSync>(failing).SyncStarterRoles());

        await hosted.StartAsync(Cancellation);
        lifetime.Started();
        await ((BackgroundService)hosted).ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

        log.Should().ContainSingle(entry => entry.Level == LogLevel.Error).Which.Error!.Message.Should().Be("the database is away");
    }

    [Fact]
    public async Task A_tenant_that_could_not_be_synced_is_logged_with_the_rest_of_the_report()
    {
        var report = new StarterRoleSyncReport(3, 2, 1, 0, [("7", new InvalidOperationException("locked"))]);
        var (hosted, lifetime, log) = Host(services => services.AddSingleton<IStarterRoleSync>(new CountingSync(report: report)).SyncStarterRoles());

        await hosted.StartAsync(Cancellation);
        lifetime.Started();
        await ((BackgroundService)hosted).ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

        report.Succeeded.Should().BeFalse();
        log.Select(entry => (entry.Level, entry.Message)).Should().BeEquivalentTo([
            (LogLevel.Error, "The roles of tenant 7 could not follow their starter roles. The host's next start tries again."),
            (LogLevel.Warning, "1 roles were left as they are, because following their starter roles would leave their tenants without an administrator. They follow once those tenants have another."),
            (LogLevel.Information, "The starter roles are synced in 3 tenants: 2 roles followed their starter roles, and 0 were made from starter roles the catalogue no longer has and kept their permissions."),
        ]);
    }

    [Fact]
    public async Task A_host_without_a_sync_to_run_is_told_what_to_register()
    {
        var (hosted, lifetime, _) = Host(services => services.SyncStarterRoles());

        await hosted.StartAsync(Cancellation);
        lifetime.Started();

        await FluentActions.Awaiting(() => ((BackgroundService)hosted).ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), Cancellation))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*services.AddTenancy<TContext>(...)*");
    }

    /// <summary>The one hosted service of services that <paramref name="configure"/> sets up, with a lifetime the test starts and a log it reads.</summary>
    private static (IHostedService Hosted, TestLifetime Lifetime, ConcurrentQueue<Entry> Log) Host(Action<IServiceCollection> configure)
    {
        var lifetime = new TestLifetime();
        var log = new ConcurrentQueue<Entry>();
        var services = new ServiceCollection()
            .AddSingleton<IHostApplicationLifetime>(lifetime)
            .AddSingleton(log)
            .AddSingleton(typeof(ILogger<>), typeof(QueueLogger<>));
        configure(services);

        services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)).Should().ContainSingle();
        return (services.BuildServiceProvider().GetServices<IHostedService>().Single(), lifetime, log);
    }

    private sealed record Entry(LogLevel Level, string Message, Exception? Error);

    private sealed class QueueLogger<T>(ConcurrentQueue<Entry> log) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => log.Enqueue(new Entry(logLevel, formatter(state, exception), exception));
    }

    private sealed class CountingSync(Exception? failure = null, StarterRoleSyncReport? report = null) : IStarterRoleSync
    {
        private int _runs;

        public int Runs => _runs;

        public TaskCompletionSource<bool> Ran { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<StarterRoleSyncReport> SyncAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _runs);
            Ran.TrySetResult(true);
            return failure is null ? Task.FromResult(report ?? new StarterRoleSyncReport(0, 0, 0, 0, [])) : Task.FromException<StarterRoleSyncReport>(failure);
        }
    }

    private sealed class TestLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();

        public CancellationToken ApplicationStarted => _started.Token;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void Started() => _started.Cancel();

        public void StopApplication()
        {
        }
    }
}
