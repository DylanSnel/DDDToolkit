using System.Text.Json;
using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.EntityFramework.Tests.Domain.Events;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.Messaging.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The receiving end of pgmq: a queue read by <see cref="PgmqConsumer"/> and handed to the modules of
/// the process through <see cref="IntegrationEventReceiver"/>, against a real Postgres with pgmq. Skipped
/// without Docker, and failed in CI, the same as <see cref="PgmqSinkTests"/>.
/// </summary>
public sealed class PgmqConsumerTests : IAsyncLifetime
{
    private PgmqDatabase? _database;

    public async ValueTask InitializeAsync() => _database = await PgmqDatabase.StartAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    private PgmqDatabase Database
    {
        get
        {
            RequiredContainers.EnforceOrSkip(
                available: _database is not null,
                RequiredContainers.Required,
                "pgmq on PostgreSQL",
                $"No Docker here, so '{PgmqDatabase.Image}' could not be started. The pgmq consumer is not covered on this machine.");

            return _database!;
        }
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static IntegrationEventMessage Message(Guid? id = null) => new()
    {
        MessageId = id ?? Guid.CreateVersion7(),
        Name = "library.shelf-opened",
        Version = 3,
        Payload = JsonSerializer.Serialize(new ShelfOpenedV3("SHELF_1", "Fiction")),
        OccurredAt = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero),
        AggregateType = "Shelf",
        AggregateId = "SHELF_1",
    };

    [Fact]
    public async Task One_message_goes_to_every_queue_that_wants_it_in_one_transaction()
    {
        var database = Database;
        var sink = new PgmqSink(NpgsqlDataSource.Create(database.ConnectionString), new PgmqSinkOptions().UseQueues(_ => ["fanout_a", "fanout_b"]));

        await sink.SendAsync(Message(), Cancellation);

        await using var connection = await database.OpenAsync(Cancellation);
        (await PgmqQueue.ReadAsync(connection, null, "fanout_a", cancellationToken: Cancellation)).Should().ContainSingle();
        (await PgmqQueue.ReadAsync(connection, null, "fanout_b", cancellationToken: Cancellation)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_message_is_handed_to_the_module_and_archived()
    {
        var shelves = new ShelfCounter();
        await using var host = await HostAsync("deliver_q", shelves);

        await host.SendAsync(Message());
        (await host.Consumer.ConsumeOnceAsync(Cancellation)).Should().Be(1);

        shelves.Seen.Should().Equal("Fiction");
        (await host.PendingAsync()).Should().Be(0, "an applied message is archived");
    }

    [Fact]
    public async Task The_same_message_delivered_twice_is_applied_once()
    {
        var shelves = new ShelfCounter();
        await using var host = await HostAsync("twice_q", shelves);
        var id = Guid.CreateVersion7();

        await host.SendAsync(Message(id));
        await host.SendAsync(Message(id));
        await host.Consumer.ConsumeOnceAsync(Cancellation);

        shelves.Seen.Should().ContainSingle("the module's inbox knows it applied that message id");
        (await host.PendingAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_message_whose_handler_fails_comes_back_after_the_visibility_timeout()
    {
        var shelves = new ShelfCounter { FailuresLeft = 1 };
        await using var host = await HostAsync("retry_q", shelves, options => options.VisibilityTimeout = TimeSpan.FromSeconds(1));

        await host.SendAsync(Message());
        await host.Consumer.ConsumeOnceAsync(Cancellation);
        shelves.Seen.Should().BeEmpty();

        await Task.Delay(TimeSpan.FromSeconds(1.5), Cancellation);
        await host.Consumer.ConsumeOnceAsync(Cancellation);

        shelves.Seen.Should().Equal("Fiction");
        (await host.PendingAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_message_that_keeps_failing_is_archived_as_poison()
    {
        var shelves = new ShelfCounter { FailuresLeft = int.MaxValue };
        await using var host = await HostAsync("poison_q", shelves, options => options.MaxDeliveries = 1);

        await host.SendAsync(Message());
        await host.Consumer.ConsumeOnceAsync(Cancellation);

        (await host.PendingAsync()).Should().Be(0, "after its last allowed delivery it leaves the queue for the archive");
        (await host.ArchivedAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_message_without_the_envelope_headers_is_archived_unread()
    {
        var shelves = new ShelfCounter();
        await using var host = await HostAsync("stranger_q", shelves);

        await using (var connection = await host.Database.OpenAsync(Cancellation))
        {
            await PgmqQueue.SendAsync(connection, null, "stranger_q", """{"ShelfId":"SHELF_1","DisplayName":"Fiction"}""", cancellationToken: Cancellation);
        }

        await host.Consumer.ConsumeOnceAsync(Cancellation);

        shelves.Seen.Should().BeEmpty("without a message id there is nothing to deduplicate on");
        (await host.PendingAsync()).Should().Be(0);
    }

    // ------------------------------------------------------------------ the process under test

    private async Task<ConsumerHost> HostAsync(string queue, ShelfCounter shelves, Action<PgmqConsumerOptions>? configure = null)
    {
        var database = Database;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDDDToolkitEntityFramework(options => options.MapIntegrationEvents(contracts => contracts.Register<ShelfOpenedV3>()));
        services.AddDbContext<PgmqContext>((provider, options) => options.UseNpgsql(database.ConnectionString).UseDDDToolkit(provider));
        services.AddModuleIntegrationEvents<PgmqContext>(module => module.Handle<ShelfOpenedV3>(shelves));

        var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PgmqContext>().Database.EnsureCreatedAsync(Cancellation);
        }

        var options = new PgmqConsumerOptions();
        configure?.Invoke(options);

        var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using (var connection = await dataSource.OpenConnectionAsync(Cancellation))
        {
            await PgmqQueue.CreateAsync(connection, null, queue, Cancellation);
        }

        var consumer = new PgmqConsumer(dataSource, queue, provider.GetRequiredService<IntegrationEventReceiver>(), options);
        return new ConsumerHost(database, provider, dataSource, queue, consumer);
    }

    private sealed class ConsumerHost(PgmqDatabase database, ServiceProvider provider, NpgsqlDataSource dataSource, string queue, PgmqConsumer consumer) : IAsyncDisposable
    {
        public PgmqDatabase Database { get; } = database;

        public PgmqConsumer Consumer { get; } = consumer;

        public Task SendAsync(IntegrationEventMessage message)
            => new PgmqSink(dataSource, new PgmqSinkOptions().UseQueue(queue)).SendAsync(message, Cancellation);

        public Task<long> PendingAsync() => CountAsync($"pgmq.q_{queue}");

        public Task<long> ArchivedAsync() => CountAsync($"pgmq.a_{queue}");

        private async Task<long> CountAsync(string table)
        {
            await using var connection = await dataSource.OpenConnectionAsync(Cancellation);
            await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection);
            return (long)(await command.ExecuteScalarAsync(Cancellation))!;
        }

        public async ValueTask DisposeAsync()
        {
            await provider.DisposeAsync();
            await dataSource.DisposeAsync();
        }
    }

    [IntegrationEventConsumer("library.shelf-counter")]
    private sealed class ShelfCounter : IIntegrationEventHandler<ShelfOpenedV3>
    {
        public List<string> Seen { get; } = [];

        public int FailuresLeft { get; set; }

        public Task HandleAsync(ShelfOpenedV3 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
        {
            if (FailuresLeft-- > 0)
            {
                throw new InvalidOperationException("Not yet.");
            }

            Seen.Add(contract.DisplayName);
            return Task.CompletedTask;
        }
    }
}
