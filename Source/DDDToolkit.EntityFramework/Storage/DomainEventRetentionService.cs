using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DDDToolkit.EntityFramework.Storage;

/// <summary>
/// Hosted service that runs <see cref="DomainEventRetention{TContext}"/> once at start and then every
/// <see cref="DomainEventRetentionOptions{TContext}.Interval"/>, each run in a service scope of its own.
/// Failures are logged and the next tick tries again. Register with
/// <c>services.AddDomainEventRetention&lt;TContext&gt;(...)</c>.
/// </summary>
public sealed class DomainEventRetentionService<TContext> : BackgroundService where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DomainEventRetentionOptions<TContext> _options;
    private readonly ILogger _logger;

    /// <summary>Creates the service.</summary>
    public DomainEventRetentionService(IServiceScopeFactory scopeFactory, DomainEventRetentionOptions<TContext> options, ILogger<DomainEventRetentionService<TContext>>? logger = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<DomainEventRetentionService<TContext>>.Instance;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.Interval);

        do
        {
            try
            {
                await DeleteExpiredAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Deleting expired outbox and inbox rows of {Context} failed; retrying after {Interval}.", typeof(TContext).Name, _options.Interval);
            }
        }
        while (await WaitForNextTickAsync(timer, stoppingToken).ConfigureAwait(false));
    }

    /// <summary>One run, in a scope of its own.</summary>
    public async Task<DomainEventRetentionResult> DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var retention = scope.ServiceProvider.GetRequiredService<DomainEventRetention<TContext>>();
        var result = await retention.DeleteExpiredAsync(cancellationToken).ConfigureAwait(false);

        if (result.OutboxMessages > 0 || result.InboxMessages > 0)
        {
            _logger.LogInformation(
                "Deleted {OutboxMessages} delivered outbox rows and {InboxMessages} inbox rows of {Context}.",
                result.OutboxMessages,
                result.InboxMessages,
                typeof(TContext).Name);
        }

        return result;
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
