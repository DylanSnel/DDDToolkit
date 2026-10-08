using System.Diagnostics;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DDDToolkit.Startup;

/// <summary>
/// Runs the host's start-up checks (<see cref="StartupChecks"/>) before anything of the host starts, the server
/// that binds its port included: in <see cref="StartingAsync"/>, which the host calls for every lifecycle service
/// before it calls any hosted service's <c>StartAsync</c>.
/// </summary>
/// <remarks>
/// One runner per host, registered where the host asks for its checks, by
/// <see cref="StartupCheckServiceCollectionExtensions.RunStartupChecks"/>. It runs the checks one after
/// the other, each as the application itself (<see cref="Caller.System"/>), begun for that check alone: a host that
/// requires explicit callers would otherwise refuse the first connection a check makes, and a check is the
/// application's own bookkeeping. What a check throws stops the start as it was thrown; the log names the check,
/// and the exception carries its name in <see cref="Exception.Data"/>, under <see cref="StartupChecks.FailedCheckKey"/>.
/// </remarks>
internal sealed class StartupCheckRunner(StartupChecks checks, IServiceProvider services, ILogger<StartupCheckRunner>? logger = null) : IHostedLifecycleService
{
    private readonly ILogger _logger = logger ?? NullLogger<StartupCheckRunner>.Instance;

    /// <inheritdoc />
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        if (checks.AllSkippedReason is { } all)
        {
            _logger.LogInformation("Every start-up check is turned off: {Reason}", all);
            return;
        }

        foreach (var name in checks.Skipped.Keys.Where(name => checks.Registered.All(check => check.Name != name)).Order(StringComparer.Ordinal))
        {
            _logger.LogWarning(
                "The start-up check '{Check}' is turned off, and no registration brings a check of that name, so it turns nothing off. The checks this host has are: {Checks}.",
                name,
                string.Join(", ", checks.Registered.Select(check => check.Name)));
        }

        // The checks that run, ordered before the first of them runs, so a contradiction among them stops the start
        // before anything was asked.
        var ordered = checks.Order(checks.Registered);

        var passed = new List<string>();
        var clock = Stopwatch.StartNew();

        foreach (var check in ordered)
        {
            if (checks.Skipped.TryGetValue(check.Name, out var reason))
            {
                _logger.LogInformation("The start-up check '{Check}' is turned off: {Reason}", check.Name, reason);
                continue;
            }

            var started = clock.Elapsed;
            try
            {
                // Begun for each check, not once around them all: a check that runs without awaiting anything runs
                // in this method's flow, so a caller it begins and leaves behind would otherwise be the caller of
                // every check after it. Ending this one puts back the caller from before it, whatever the check did.
                using (Callers.Begin(Caller.System))
                {
                    await check.RunAsync(services, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failure.Data[StartupChecks.FailedCheckKey] = check.Name;
                _logger.LogError("The start-up check '{Check}' refused the start. What it found follows, as the host gives up.", check.Name);
                throw;
            }

            _logger.LogDebug("The start-up check '{Check}' passed in {Milliseconds} ms.", check.Name, (long)(clock.Elapsed - started).TotalMilliseconds);
            passed.Add(check.Name);
        }

        if (passed.Count > 0)
        {
            _logger.LogInformation("The start-up checks passed in {Milliseconds} ms: {Checks}.", clock.ElapsedMilliseconds, string.Join(", ", passed));
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
