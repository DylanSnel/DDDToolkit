using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DDDToolkit.Examples.AppHost.Tests;

/// <summary>
/// One sample, started once for all the scenarios run against it: its AppHost, its containers and its
/// hosts. Starting SQL Server or Postgres takes seconds, so a fixture per test would make the suite
/// minutes long for nothing.
/// </summary>
/// <typeparam name="TAppHost">The AppHost's generated <c>Projects.*</c> type.</typeparam>
public abstract class ShopFixture<TAppHost> : IAsyncLifetime where TAppHost : class
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(5);

    private DistributedApplication? _app;

    /// <summary>The resource that serves the shop's HTTP API: the monolith, or the storefront service.</summary>
    protected abstract string ShopResource { get; }

    /// <summary>The resources that have to be healthy before a scenario may run.</summary>
    protected virtual IEnumerable<string> ResourcesToWaitFor => [ShopResource];

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
            [],
            (options, settings) => options.DisableDashboard = true,
            TestContext.Current.CancellationToken);

        builder.Services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

        _app = await builder.BuildAsync(TestContext.Current.CancellationToken);

        using var starting = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        starting.CancelAfter(StartTimeout);

        await _app.StartAsync(starting.Token);

        foreach (var resource in ResourcesToWaitFor)
        {
            await _app.ResourceNotifications.WaitForResourceHealthyAsync(resource, starting.Token);
        }
    }

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
/// suites, unless <c>DDDTOOLKIT_REQUIRE_CONTAINERS=1</c> says a missing Docker is an error, as it is in CI.
/// </summary>
internal static class RequiredDocker
{
    public static readonly Lazy<bool> Available = new(IsRunning);

    public static void SkipOrFail()
    {
        if (Environment.GetEnvironmentVariable("DDDTOOLKIT_REQUIRE_CONTAINERS") == "1")
        {
            throw new InvalidOperationException("Docker is not running, and DDDTOOLKIT_REQUIRE_CONTAINERS=1 requires it.");
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
