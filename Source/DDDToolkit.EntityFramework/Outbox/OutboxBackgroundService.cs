using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DDDToolkit.EntityFramework.Outbox;

/// <summary>Polling settings for <see cref="OutboxBackgroundService{TContext}"/>.</summary>
public sealed class OutboxBackgroundServiceOptions<TContext> where TContext : DbContext
{
    /// <summary>How long to wait between polls when the outbox is empty. Defaults to five seconds.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How many messages one <see cref="OutboxProcessor{TContext}.ProcessPendingAsync"/> call takes. Defaults to 100.</summary>
    public int BatchSize { get; set; } = 100;
}

/// <summary>
/// Hosted service that polls the outbox of <typeparamref name="TContext"/> every
/// <see cref="OutboxBackgroundServiceOptions{TContext}.PollingInterval"/>, draining it batch by batch
/// while messages keep being delivered. Each batch runs in its own service scope, so the processor
/// and the handlers get a fresh <typeparamref name="TContext"/>. Failures are logged and the next
/// tick tries again. Register with <c>services.AddOutboxBackgroundService&lt;TContext&gt;(interval)</c>.
/// </summary>
public sealed class OutboxBackgroundService<TContext> : BackgroundService where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxBackgroundServiceOptions<TContext> _options;
    private readonly ILogger _logger;

    /// <summary>Creates the service.</summary>
    public OutboxBackgroundService(IServiceScopeFactory scopeFactory, OutboxBackgroundServiceOptions<TContext> options, ILogger<OutboxBackgroundService<TContext>>? logger = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<OutboxBackgroundService<TContext>>.Instance;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.PollingInterval);

        do
        {
            try
            {
                await DrainAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Processing the outbox of {Context} failed; retrying after {Interval}.", typeof(TContext).Name, _options.PollingInterval);
            }
        }
        while (await WaitForNextTickAsync(timer, stoppingToken).ConfigureAwait(false));
    }

    /// <summary>Processes batches until one delivers nothing.</summary>
    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        int delivered;
        do
        {
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor<TContext>>();
            delivered = await processor.ProcessPendingAsync(_options.BatchSize, cancellationToken).ConfigureAwait(false);
        }
        while (delivered > 0 && !cancellationToken.IsCancellationRequested);
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
