using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Tests.Domain;
using DDDToolkit.EntityFramework.Tests.Domain.Events;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// When the processor tries a failed message again, and what that leaves for the messages behind it.
/// <para>
/// Without a wait, a failed row stays at the head of the queue. Every poll loads the same failing rows
/// again, so the rows behind them are not reached until those have used up every attempt, and a busy
/// drain spends a failing row's attempts in milliseconds. <see cref="OutboxMessage.NextAttemptAt"/> is
/// that wait.
/// </para>
/// </summary>
public sealed class OutboxRetryTests : IDisposable
{
    private readonly SqliteDatabase _db = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly OutageSink _sink = new();

    public void Dispose() => _db.Dispose();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private TestHost CreateHost(Action<OutboxOptions>? outbox = null)
        => new(
            _db,
            options =>
            {
                options.TimeProvider = _clock;
                options.UseOutbox(o =>
                {
                    o.RegisterEvent<ShelfCreated>();
                    o.SendTo(_sink);
                    outbox?.Invoke(o);
                });
            },
            dispatchThroughRecorder: false,
            services => services.AddOutboxProcessor<LibraryContext>());

    /// <summary>One save per shelf, a millisecond apart, so the outbox holds them oldest first in this order.</summary>
    private async Task SaveShelvesAsync(TestHost host, params string[] names)
    {
        foreach (var name in names)
        {
            await host.InScopeAsync(async context =>
            {
                context.Shelves.Add(new Shelf(ShelfId.CreateUnique(), name, UserId.CreateUnique(), CatId.CreateUnique(), null));
                await context.SaveChangesAsync();
            });

            _clock.Advance(TimeSpan.FromMilliseconds(1));
        }
    }

    private static Task<int> ProcessAsync(TestHost host, int batchSize)
        => host.InScopeAsync((_, services) => services.GetRequiredService<OutboxProcessor<LibraryContext>>().ProcessPendingAsync(batchSize, Cancellation));

    private static OutboxBackgroundService<LibraryContext> BackgroundService(TestHost host, int batchSize)
        => new(host.Services.GetRequiredService<IServiceScopeFactory>(), new OutboxBackgroundServiceOptions<LibraryContext> { BatchSize = batchSize });

    private OutboxMessage Row(string shelf)
    {
        using var check = _db.CreateLibraryContext();
        return check.Outbox.AsEnumerable().Single(m => m.Payload.Contains($"\"Name\":\"{shelf}\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failing_messages_at_the_head_do_not_hold_back_the_message_behind_them()
    {
        // More failing rows than one batch holds, all older than a message the sink would take. Without a
        // wait every poll loads the same two failing rows, and the healthy one is not reached until they
        // have used up all ten attempts.
        using var host = CreateHost();
        _sink.Unreachable("broken 1", "broken 2", "broken 3");
        await SaveShelvesAsync(host, "broken 1", "broken 2", "broken 3", "healthy");

        (await ProcessAsync(host, batchSize: 2)).Should().Be(0, "the two oldest rows both fail");
        (await ProcessAsync(host, batchSize: 2)).Should().Be(1, "they are waiting now, so the next poll reaches past them");

        _sink.Delivered.Should().Equal("healthy");
        Row("broken 1").Attempts.Should().Be(1);
        Row("broken 2").Attempts.Should().Be(1);
        Row("broken 3").Attempts.Should().Be(1);
    }

    [Fact]
    public async Task A_failed_message_is_not_tried_again_before_its_NextAttemptAt()
    {
        using var host = CreateHost();
        _sink.Unreachable("broken");
        await SaveShelvesAsync(host, "broken");

        await ProcessAsync(host, batchSize: 10);

        var row = Row("broken");
        row.Attempts.Should().Be(1);
        row.NextAttemptAt.Should().Be(_clock.GetUtcNow() + TimeSpan.FromSeconds(5));

        _clock.Advance(TimeSpan.FromSeconds(5) - TimeSpan.FromMilliseconds(1));
        await ProcessAsync(host, batchSize: 10);
        _sink.Attempts.Should().Be(1, "a millisecond early is still early");

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        await ProcessAsync(host, batchSize: 10);
        _sink.Attempts.Should().Be(2, "at NextAttemptAt the row is due");

        row = Row("broken");
        row.Attempts.Should().Be(2);
        row.NextAttemptAt.Should().Be(_clock.GetUtcNow() + TimeSpan.FromSeconds(25), "the wait grows with every attempt");
    }

    [Fact]
    public async Task A_delivered_message_has_no_NextAttemptAt()
    {
        using var host = CreateHost();
        _sink.Down = true;
        await SaveShelvesAsync(host, "late");
        await ProcessAsync(host, batchSize: 10);

        _sink.Down = false;
        _clock.Advance(TimeSpan.FromSeconds(5));
        (await ProcessAsync(host, batchSize: 10)).Should().Be(1);

        var row = Row("late");
        row.ProcessedAt.Should().Be(_clock.GetUtcNow());
        row.NextAttemptAt.Should().BeNull();
        row.LastError.Should().BeNull();
    }

    [Fact]
    public async Task The_last_attempt_leaves_no_NextAttemptAt_so_resetting_Attempts_retries_at_once()
    {
        using var host = CreateHost(outbox => outbox.MaxAttempts = 2);
        _sink.Down = true;
        await SaveShelvesAsync(host, "poison");

        await ProcessAsync(host, batchSize: 10);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await ProcessAsync(host, batchSize: 10);

        var row = Row("poison");
        row.Attempts.Should().Be(2);
        row.NextAttemptAt.Should().BeNull("there is no next attempt");

        // What the documentation tells an operator to do once the cause is fixed.
        _sink.Down = false;
        await using (var context = _db.CreateLibraryContext())
        {
            await context.Outbox.ExecuteUpdateAsync(setters => setters.SetProperty(m => m.Attempts, 0), Cancellation);
        }

        (await ProcessAsync(host, batchSize: 10)).Should().Be(1, "the reset row is due without waiting");
    }

    [Fact]
    public async Task RetryDelay_decides_how_long_a_failed_message_waits()
    {
        using var host = CreateHost(outbox => outbox.RetryDelay = attempts => TimeSpan.FromMinutes(attempts));
        _sink.Down = true;
        await SaveShelvesAsync(host, "a");

        await ProcessAsync(host, batchSize: 10);
        Row("a").NextAttemptAt.Should().Be(_clock.GetUtcNow() + TimeSpan.FromMinutes(1));

        _clock.Advance(TimeSpan.FromMinutes(1));
        await ProcessAsync(host, batchSize: 10);
        Row("a").NextAttemptAt.Should().Be(_clock.GetUtcNow() + TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task A_RetryDelay_of_zero_tries_again_on_the_next_poll()
    {
        using var host = CreateHost(outbox => outbox.RetryDelay = _ => TimeSpan.Zero);
        _sink.Down = true;
        await SaveShelvesAsync(host, "a");

        await ProcessAsync(host, batchSize: 10);
        await ProcessAsync(host, batchSize: 10);

        _sink.Attempts.Should().Be(2);
        Row("a").NextAttemptAt.Should().Be(_clock.GetUtcNow());
    }

    [Fact]
    public async Task A_RetryDelay_past_the_end_of_the_calendar_still_records_the_attempt()
    {
        using var host = CreateHost(outbox => outbox.RetryDelay = _ => TimeSpan.MaxValue);
        _sink.Down = true;
        await SaveShelvesAsync(host, "a");

        await ProcessAsync(host, batchSize: 10);

        var row = Row("a");
        row.Attempts.Should().Be(1);
        row.NextAttemptAt.Should().Be(DateTimeOffset.MaxValue);
        row.LastError.Should().Contain("cannot be delivered");
    }

    [Fact]
    public void The_default_wait_starts_at_five_seconds_and_grows_fivefold_up_to_ten_minutes()
    {
        Enumerable.Range(1, 6).Select(OutboxOptions.DefaultRetryDelay).Should().Equal(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(25),
            TimeSpan.FromSeconds(125),
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(10));

        OutboxOptions.DefaultRetryDelay(int.MaxValue).Should().Be(TimeSpan.FromMinutes(10), "a large MaxAttempts must not overflow");
        new OutboxOptions().RetryDelay(3).Should().Be(TimeSpan.FromSeconds(125));

        var unset = () => new OutboxOptions().RetryDelay = null!;
        unset.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task A_busy_drain_tries_a_failing_message_once_instead_of_in_every_batch()
    {
        // The failing row is the oldest, so without a wait it heads every batch of the drain, and every
        // batch that also delivers something keeps the drain going.
        using var host = CreateHost();
        _sink.Unreachable("broken");
        await SaveShelvesAsync(host, "broken", "a", "b", "c", "d", "e");

        await BackgroundService(host, batchSize: 2).DrainAsync(Cancellation);

        _sink.Delivered.Should().Equal("a", "b", "c", "d", "e");
        Row("broken").Attempts.Should().Be(1);
    }

    [Fact]
    public async Task A_short_outage_costs_one_attempt_and_the_message_goes_out_once_the_sink_is_back()
    {
        using var host = CreateHost();
        await SaveShelvesAsync(host, "during the outage");
        var service = BackgroundService(host, batchSize: 10);

        // Three seconds of outage, polled every 100 ms. Without a wait that is thirty attempts at a row
        // that allows ten, and the row is dead before the sink comes back.
        _sink.Down = true;
        for (var poll = 0; poll < 30; poll++)
        {
            await service.DrainAsync(Cancellation);
            _clock.Advance(TimeSpan.FromMilliseconds(100));
        }

        _sink.Attempts.Should().Be(1);

        _sink.Down = false;
        _clock.Advance(TimeSpan.FromSeconds(2));
        await service.DrainAsync(Cancellation);

        _sink.Delivered.Should().Equal("during the outage");
        var row = Row("during the outage");
        row.Attempts.Should().Be(2, "one attempt during the outage and one after it");
        row.ProcessedAt.Should().Be(_clock.GetUtcNow());
        row.NextAttemptAt.Should().BeNull();
        row.LastError.Should().BeNull();
    }

    [Fact]
    public async Task A_drain_stops_when_every_message_left_is_waiting()
    {
        // A batch that delivers nothing ends the drain, so a sink that is down gets one batch a poll to
        // answer rather than the whole table in a loop.
        using var host = CreateHost();
        _sink.Down = true;
        await SaveShelvesAsync(host, "a", "b", "c", "d", "e");
        var service = BackgroundService(host, batchSize: 2);

        await service.DrainAsync(Cancellation);
        _sink.Attempts.Should().Be(2);

        await service.DrainAsync(Cancellation);
        await service.DrainAsync(Cancellation);
        await service.DrainAsync(Cancellation);
        _sink.Attempts.Should().Be(5, "each poll takes the next batch that is due, and then none is");
    }

    /// <summary>
    /// A transport that can be down for everything, or unreachable for some messages only, which is how a
    /// destination that refuses one message, or a contract that no longer serializes, looks from here.
    /// </summary>
    private sealed class OutageSink : IIntegrationEventSink
    {
        private readonly HashSet<string> _unreachable = [];
        private readonly List<string> _delivered = [];

        public bool Down { get; set; }

        /// <summary>How often <see cref="SendAsync"/> was entered, refusals included.</summary>
        public int Attempts { get; private set; }

        public IReadOnlyList<string> Delivered => _delivered;

        public void Unreachable(params string[] shelves) => _unreachable.UnionWith(shelves);

        public Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default)
        {
            Attempts++;

            var shelf = ((ShelfCreated)message.Body!).Name;
            if (Down || _unreachable.Contains(shelf))
            {
                throw new InvalidOperationException($"'{shelf}' cannot be delivered");
            }

            _delivered.Add(shelf);
            return Task.CompletedTask;
        }
    }
}
