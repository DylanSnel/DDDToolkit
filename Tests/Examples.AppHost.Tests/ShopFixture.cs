using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Examples.AppHost.Tests;

/// <summary>
/// One sample, started once for all the scenarios run against it: its AppHost, its containers and its
/// hosts. Starting SQL Server or Postgres takes seconds, so a fixture per test would make the suite
/// minutes long for nothing.
/// </summary>
/// <typeparam name="TAppHost">The AppHost's generated <c>Projects.*</c> type.</typeparam>
public abstract class ShopFixture<TAppHost> : IAsyncLifetime where TAppHost : class
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(Environment.GetEnvironmentVariable("DDDTOOLKIT_SAMPLE_START_MINUTES") is { } minutes ? double.Parse(minutes, System.Globalization.CultureInfo.InvariantCulture) : 5);

    private DistributedApplication? _app;

    /// <summary>Every resource's log lines while the sample starts, by the resource's name.</summary>
    private Dictionary<string, System.Collections.Concurrent.ConcurrentQueue<string>> _logs = [];

    /// <summary>The resource that serves the shop's HTTP API: the monolith, or the storefront service.</summary>
    protected abstract string ShopResource { get; }

    /// <summary>The resources that have to be healthy before a scenario may run.</summary>
    protected virtual IEnumerable<string> ResourcesToWaitFor => [ShopResource];

    /// <summary>What the AppHost is started with, as on its command line: a variant of the sample, for instance.</summary>
    protected virtual string[] Arguments => [];

    /// <summary>An HTTP client for <paramref name="resource"/>, the shop's own when left out.</summary>
    public HttpClient Client(string? resource = null)
        => (_app ?? throw new InvalidOperationException("The sample has not started."))
            .CreateHttpClient(resource ?? ShopResource);

    /// <summary>Skips the calling scenario when Docker is not there to start the sample in.</summary>
    public void SkipUnlessStarted()
    {
        if (_app is null)
        {
            RequiredDocker.SkipOrFail();
        }
    }

    public async ValueTask InitializeAsync()
    {
        if (!RequiredDocker.Available.Value)
        {
            // Each scenario asks SkipUnlessStarted() first. A skip thrown from here would fail them all.
            return;
        }

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<TAppHost>(
            Arguments,
            (options, settings) => options.DisableDashboard = true,
            TestContext.Current.CancellationToken);

        builder.Services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

        _app = await builder.BuildAsync(TestContext.Current.CancellationToken);

        // Every resource's log, kept while the sample starts, so a sample that never becomes healthy says
        // why instead of timing out in silence. A log is kept under the id of the resource's instance, which is
        // known only once the instance is made, so each is watched from the first notice of that id.
        using var watching = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _logs = _app.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .ToDictionary(resource => resource.Name, _ => new System.Collections.Concurrent.ConcurrentQueue<string>());
        var loggers = _app.Services.GetRequiredService<ResourceLoggerService>();
        var notifications = _app.ResourceNotifications;
        _ = Task.Run(async () =>
        {
            HashSet<string> watched = [];
            try
            {
                await foreach (var notice in notifications.WatchAsync(watching.Token))
                {
                    if (watched.Add(notice.ResourceId) && _logs.TryGetValue(notice.Resource.Name, out var lines))
                    {
                        _ = WatchLogAsync(loggers, notice.ResourceId, lines, watching.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        using var starting = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        starting.CancelAfter(StartTimeout);

        try
        {
            await _app.StartAsync(starting.Token);

            foreach (var resource in ResourcesToWaitFor)
            {
                await _app.ResourceNotifications.WaitForResourceHealthyAsync(resource, starting.Token);
            }
        }
        catch (OperationCanceledException) when (starting.IsCancellationRequested && !TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The sample did not become healthy within {StartTimeout}.{Environment.NewLine}{Report()}");
        }
        catch (Exception failed) when (failed is not OperationCanceledException && !TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            // A resource that fails to start, a host whose start-up check refused, say, ends the wait at once: the
            // same report then says why, where the exception alone says only that it failed. The last lines of a
            // process that just exited may still be on their way.
            await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            throw new InvalidOperationException($"The sample did not start: {failed.Message}{Environment.NewLine}{Report()}", failed);
        }
        finally
        {
            await watching.CancelAsync();
        }
    }

    /// <summary>Keeps the lines of the log of one instance of a resource, until <paramref name="cancellationToken"/>.</summary>
    private static async Task WatchLogAsync(ResourceLoggerService loggers, string resourceId, System.Collections.Concurrent.ConcurrentQueue<string> lines, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var batch in loggers.WatchAsync(resourceId).WithCancellation(cancellationToken))
            {
                foreach (var line in batch)
                {
                    lines.Enqueue(line.Content);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>The last lines of every resource's log while the sample started, each with its state.</summary>
    private string Report()
        => string.Join(Environment.NewLine + Environment.NewLine, _logs.Select(log =>
            $"--- {log.Key} ({State(log.Key)}), last lines:{Environment.NewLine}{string.Join(Environment.NewLine, log.Value.TakeLast(25))}"));

    private string State(string resource)
        => _app!.ResourceNotifications.TryGetCurrentState(resource, out var state)
            ? $"{state.Snapshot.State?.Text ?? "no state"}, health {state.Snapshot.HealthStatus?.ToString() ?? "unknown"}"
            : "never seen";

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// The samples need Docker. Without it the suite is skipped rather than failed, the same as the provider
/// suites, unless <c>DDDTOOLKIT_REQUIRE_CONTAINERS</c> says a missing Docker is an error, as it is in CI: by the
/// one reading of that variable every suite with containers shares, so <c>1</c>, <c>true</c> and <c>yes</c> all
/// say so here as they do there.
/// </summary>
internal static class RequiredDocker
{
    public static readonly Lazy<bool> Available = new(IsRunning);

    public static void SkipOrFail()
    {
        if (RequiredContainers.Required)
        {
            throw new InvalidOperationException($"Docker is not running, and {RequiredContainers.Variable} requires it.");
        }

        Assert.Skip("Docker is not running.");
    }

    private static bool IsRunning()
    {
        try
        {
            using var docker = Process.Start(new ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });

            if (docker is null || !docker.WaitForExit(TimeSpan.FromSeconds(20)))
            {
                return false;
            }

            return docker.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
