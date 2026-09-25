using System.Diagnostics;
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
/// Reading with <c>pgmq.read_with_poll</c>: a read that waits inside Postgres for a message instead of
/// coming back empty, and the consumer loop that uses it. On pgmq 1.5.1, what Supabase ships, because the
/// function has to be there on the oldest version the package supports. Skipped without Docker, failed in
/// CI, as the other pgmq tests.
/// </summary>
public sealed class PgmqLongPollTests(PgmqLongPollTests.Server server) : IClassFixture<PgmqLongPollTests.Server>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private PgmqDatabase Database
    {
        get
        {
            RequiredContainers.EnforceOrSkip(
                available: server.Database is not null,
                RequiredContainers.Required,
                "pgmq on PostgreSQL",
                $"No Docker here, so '{PgmqDatabase.SupabaseImage}' could not be started. Long polling is not covered on this machine.");

            return server.Database!;
        }
    }

    [Fact]
    public async Task A_long_poll_returns_as_soon_as_a_message_arrives()
    {
        var database = Database;
        await using var reader = await database.OpenAsync(Cancellation);
        await PgmqQueue.CreateAsync(reader, null, "arrives_q", Cancellation);

        var clock = Stopwatch.StartNew();
        var read = PgmqQueue.ReadWithPollAsync(reader, null, "arrives_q", maxPollSeconds: 20, cancellationToken: Cancellation);

        await Task.Delay(TimeSpan.FromSeconds(1), Cancellation);
        await using (var sender = await database.OpenAsync(Cancellation))
        {
            await PgmqQueue.SendAsync(sender, null, "arrives_q", """{"a":1}""", cancellationToken: Cancellation);
        }

        (await read).Should().ContainSingle("the read was still waiting when the message was committed");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "it returned when the message came, not when the 20 seconds ran out");
    }

    [Fact]
    public async Task A_long_poll_on_an_empty_queue_comes_back_empty_when_the_wait_is_over()
    {
        await using var connection = await Database.OpenAsync(Cancellation);
        await PgmqQueue.CreateAsync(connection, null, "empty_q", Cancellation);

        var clock = Stopwatch.StartNew();
        var read = await PgmqQueue.ReadWithPollAsync(connection, null, "empty_q", maxPollSeconds: 1, cancellationToken: Cancellation);

        read.Should().BeEmpty();
        clock.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(0.9), "it waited in Postgres before giving up");
    }

    [Fact]
    public async Task A_long_poll_longer_than_the_command_timeout_is_not_cut_short()
    {
        var shortTimeout = new NpgsqlConnectionStringBuilder(Database.ConnectionString) { CommandTimeout = 1 }.ConnectionString;
        await using var connection = new NpgsqlConnection(shortTimeout);
        await connection.OpenAsync(Cancellation);
        await PgmqQueue.CreateAsync(connection, null, "timeout_q", Cancellation);

        var read = () => PgmqQueue.ReadWithPollAsync(connection, null, "timeout_q", maxPollSeconds: 3, cancellationToken: Cancellation);

        (await read.Should().NotThrowAsync()).Which.Should().BeEmpty("the wait is added to the command timeout, which still bounds the read itself");
    }

    [Fact]
    public async Task A_long_poll_without_a_wait_is_refused()
    {
        await using var connection = new NpgsqlConnection();

        var read = () => PgmqQueue.ReadWithPollAsync(connection, null, "any_q", maxPollSeconds: 0, cancellationToken: Cancellation);

        await read.Should().ThrowAsync<ArgumentOutOfRangeException>("pgmq looks at the clock first and would never read at all");
    }

    [Fact]
    public async Task The_running_consumer_picks_a_message_up_without_waiting_for_its_polling_interval()
    {
        var shelves = new ShelfCounter();
        await using var host = await HostAsync("running_q", shelves, options =>
        {
            options.LongPollTimeout = TimeSpan.FromSeconds(5);
            options.PollingInterval = TimeSpan.FromMinutes(1);
        });

        await host.Consumer.StartAsync(Cancellation);
        await Task.Delay(TimeSpan.FromSeconds(1), Cancellation);
        await host.SendAsync();

        await shelves.WaitAsync(TimeSpan.FromSeconds(5));
        await host.Consumer.StopAsync(Cancellation);

        shelves.Seen.Should().Equal("Fiction");
    }

    [Fact]
    public async Task With_long_polling_off_the_running_consumer_waits_its_polling_interval()
    {
        var shelves = new ShelfCounter();
        await using var host = await HostAsync("short_q", shelves, options =>
        {
            options.LongPollTimeout = TimeSpan.Zero;
            options.PollingInterval = TimeSpan.FromMinutes(1);
        });

        await host.Consumer.StartAsync(Cancellation);
        await Task.Delay(TimeSpan.FromSeconds(1), Cancellation);
        await host.SendAsync();

        await Task.Delay(TimeSpan.FromSeconds(2), Cancellation);
        await host.Consumer.StopAsync(Cancellation);

        shelves.Seen.Should().BeEmpty("the first read found nothing, and the next one is a minute away");
    }

    [Fact]
    public async Task Stopping_the_consumer_ends_a_long_poll_that_is_waiting()
    {
        await using var host = await HostAsync("stopping_q", new ShelfCounter(), options => options.LongPollTimeout = TimeSpan.FromMinutes(1));

        await host.Consumer.StartAsync(Cancellation);
        await Task.Delay(TimeSpan.FromSeconds(1), Cancellation);

        var clock = Stopwatch.StartNew();
        await host.Consumer.StopAsync(Cancellation);

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "stopping cancels the read in Postgres rather than waiting out the minute");
    }

    [Fact]
    public async Task ConsumeOnce_does_not_wait_even_with_long_polling_on()
    {
        await using var host = await HostAsync("once_q", new ShelfCounter(), options => options.LongPollTimeout = TimeSpan.FromMinutes(1));

        var clock = Stopwatch.StartNew();
        var read = await host.Consumer.ConsumeOnceAsync(Cancellation);

        read.Should().Be(0);
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "a caller polling by hand gets its answer at once");
    }

    // ------------------------------------------------------------------ the process under test

    private async Task<ConsumerHost> HostAsync(string queue, ShelfCounter shelves, Action<PgmqConsumerOptions> configure)
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
        configure(options);

        var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        var consumer = new PgmqConsumer(dataSource, queue, provider.GetRequiredService<IntegrationEventReceiver>(), options);

        // The queue exists before the test sends anything, as it would once the consumer had started.
        await using (var connection = await dataSource.OpenConnectionAsync(Cancellation))
        {
            await PgmqQueue.CreateAsync(connection, null, queue, Cancellation);
        }

        return new ConsumerHost(provider, dataSource, queue, consumer);
    }

    private sealed class ConsumerHost(ServiceProvider provider, NpgsqlDataSource dataSource, string queue, PgmqConsumer consumer) : IAsyncDisposable
    {
        public PgmqConsumer Consumer { get; } = consumer;

        public Task SendAsync()
            => new PgmqSink(dataSource, new PgmqSinkOptions().UseQueue(queue)).SendAsync(
                new IntegrationEventMessage
                {
                    MessageId = Guid.CreateVersion7(),
                    Name = "library.shelf-opened",
                    Version = 3,
                    Payload = JsonSerializer.Serialize(new ShelfOpenedV3("SHELF_1", "Fiction")),
                    OccurredAt = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero),
                    AggregateType = "Shelf",
                    AggregateId = "SHELF_1",
                },
                Cancellation);

        public async ValueTask DisposeAsync()
        {
            Consumer.Dispose();
            await provider.DisposeAsync();
            await dataSource.DisposeAsync();
        }
    }

    [IntegrationEventConsumer("library.shelf-watcher")]
    private sealed class ShelfCounter : IIntegrationEventHandler<ShelfOpenedV3>
    {
        private readonly TaskCompletionSource _seen = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> Seen { get; } = [];

        public Task HandleAsync(ShelfOpenedV3 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
        {
            Seen.Add(contract.DisplayName);
            _seen.TrySetResult();
            return Task.CompletedTask;
        }

        /// <summary>Waits for the first message, failing the test if it does not come in time.</summary>
        public Task WaitAsync(TimeSpan timeout) => _seen.Task.WaitAsync(timeout, Cancellation);
    }

    /// <summary>pgmq 1.5.1, started once for the class; every test uses a queue of its own.</summary>
    public sealed class Server : IAsyncLifetime
    {
        public PgmqDatabase? Database { get; private set; }

        public async ValueTask InitializeAsync() => Database = await PgmqDatabase.StartAsync(PgmqDatabase.SupabaseImage, TestContext.Current.CancellationToken);

        public async ValueTask DisposeAsync()
        {
            if (Database is not null)
            {
                await Database.DisposeAsync();
            }
        }
    }
}
