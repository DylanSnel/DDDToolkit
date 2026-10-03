using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// The three modules' outboxes, read through a host's own services: for whoever has to know that every event the
/// modules stored has been handled, before counting what a poller did or before the database is copied.
/// </summary>
/// <remarks>
/// An outbox is the toolkit's own bookkeeping and belongs to no tenant, so it is read as the toolkit's system
/// caller, begun here: the host refuses work that names no caller, and the database gives the rows to the
/// bookkeeping role alone.
/// </remarks>
public static class Outboxes
{
    /// <summary>How long the outboxes get to drain: many times their one-second polling interval.</summary>
    public static readonly TimeSpan LongestDrain = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Waits until every module's poller has handled every row its outbox holds, and returns the rows by the
    /// name of their table. Fails when that takes longer than <see cref="LongestDrain"/>, naming the events
    /// that are left and what the host logged: whoever went on would take the pollers' work for its own.
    /// </summary>
    /// <param name="host">The host, started.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<OutboxMessage>>> DrainedAsync(SampleFactory host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);

        var deadline = DateTimeOffset.UtcNow + LongestDrain;
        while (true)
        {
            var outboxes = await ReadAsync(host, cancellationToken);
            var waiting = outboxes.SelectMany(outbox => outbox.Value.Where(row => row.ProcessedAt == null).Select(row => $"{outbox.Key}: {row.EventName}")).ToList();
            if (waiting.Count == 0)
            {
                return outboxes;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException(
                    $"The outboxes still hold events nobody handled after {LongestDrain.TotalSeconds:0} seconds: {string.Join(", ", waiting)}. The host logged:{Environment.NewLine}{host.Errors.Describe()}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }

    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<OutboxMessage>>> ReadAsync(SampleFactory host, CancellationToken cancellationToken)
    {
        using (Callers.Begin(Caller.System))
        {
            await using var scope = host.Services.CreateAsyncScope();
            return new Dictionary<string, IReadOnlyList<OutboxMessage>>(StringComparer.Ordinal)
            {
                [TenantsContext.OutboxTable] = await RowsAsync(scope.ServiceProvider.GetRequiredService<TenantsContext>(), cancellationToken),
                [ProjectsContext.OutboxTable] = await RowsAsync(scope.ServiceProvider.GetRequiredService<ProjectsContext>(), cancellationToken),
                [InspectionsContext.OutboxTable] = await RowsAsync(scope.ServiceProvider.GetRequiredService<InspectionsContext>(), cancellationToken),
            };
        }
    }

    private static async Task<IReadOnlyList<OutboxMessage>> RowsAsync(DbContext context, CancellationToken cancellationToken)
        => await context.Set<OutboxMessage>().AsNoTracking().ToListAsync(cancellationToken);
}
