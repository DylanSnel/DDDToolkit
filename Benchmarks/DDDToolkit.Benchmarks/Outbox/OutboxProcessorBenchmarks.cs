using System.Text.Json;
using BenchmarkDotNet.Attributes;
using DDDToolkit.Benchmarks.Infrastructure;
using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.EntityFramework.Outbox;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Benchmarks.Outbox;

/// <summary>
/// One <see cref="OutboxProcessor{TContext}.ProcessPendingAsync"/> call that delivers <see cref="Messages"/>
/// pending rows to a sink that does nothing. The processor saves the mark of every message on its own, so the
/// cost of one save is paid once per message. The sink is not registered in the container, so the processor
/// constructs it.
/// <para>
/// Each operation first makes every row pending again with one <c>ExecuteUpdate</c>. <see cref="MakePendingOnly"/>
/// measures that statement alone.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class OutboxProcessorBenchmarks
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _services = null!;

    /// <summary>How many pending rows the batch delivers.</summary>
    [Params(10, 100, 1_000)]
    public int Messages { get; set; }

    /// <summary>Creates the database with <see cref="Messages"/> rows, and a container with the toolkit and the outbox.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var collection = new ServiceCollection();
        collection.AddDDDToolkitEntityFramework(options => options.UseOutbox(outbox => outbox
            .RegisterEvent<OrderNoted>("order-noted", 1)
            .SendTo<DiscardingSink>()));
        collection.AddDbContext<OutboxContext>((services, options) => options.UseSqlite(_connection).UseDDDToolkit(services));
        collection.AddOutboxProcessor<OutboxContext>();
        _services = collection.BuildServiceProvider();

        using var seed = Plain();
        seed.Database.EnsureCreated();

        var json = _services.GetRequiredService<DDDEntityFrameworkOptions>().OutboxFor(typeof(OutboxContext))!.JsonOptions;
        var start = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < Messages; i++)
        {
            var noted = new OrderNoted("reference " + i);
            seed.Outbox.Add(new OutboxMessage
            {
                Id = noted.EventId,
                EventName = "order-noted",
                Payload = JsonSerializer.Serialize(noted, json),
                OccurredAt = noted.OccurredAt,
                CreatedAt = start.AddTicks(i),
            });
        }

        seed.SaveChanges();
    }

    /// <summary>Disposes the container and closes the database.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    /// <summary>Makes every row pending again, without a delivery.</summary>
    [Benchmark]
    public int MakePendingOnly() => MakePending();

    /// <summary>Makes every row pending again, and delivers them in one batch.</summary>
    [Benchmark]
    public async Task<int> ProcessPending()
    {
        MakePending();
        await using var scope = _services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor<OutboxContext>>();
        return await processor.ProcessPendingAsync(Messages);
    }

    private int MakePending()
    {
        using var context = Plain();
        return context.Outbox.ExecuteUpdate(row => row
            .SetProperty(message => message.ProcessedAt, (DateTimeOffset?)null)
            .SetProperty(message => message.Attempts, 0));
    }

    private OutboxContext Plain() => new(new DbContextOptionsBuilder<OutboxContext>().UseSqlite(_connection).Options);
}
