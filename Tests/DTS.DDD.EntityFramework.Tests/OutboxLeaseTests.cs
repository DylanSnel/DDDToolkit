using System.Collections.Concurrent;
using DTS.DDD.BaseTypes;
using DTS.DDD.EntityFramework.Options;
using DTS.DDD.EntityFramework.Outbox;
using DTS.DDD.EntityFramework.Tests.Domain;
using DTS.DDD.EntityFramework.Tests.Domain.Events;
using DTS.DDD.EntityFramework.Tests.Infrastructure;
using DTS.DDD.ExampleApi.Domain.UserAggregate.ValueObjects;
using DTS.DDD.ExampleLibrary.Common.ValueObjects;
using DTS.DDD.Interfaces;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DTS.DDD.EntityFramework.Tests;

/// <summary>
/// The lease on a claimed batch: <see cref="OutboxMessage.LockedBy"/> and <see cref="OutboxMessage.LockedUntil"/>.
/// <para>
/// Another processor is played by a plain context on the same database that writes its own claim into a row,
/// before the batch or while a handler runs. The last test runs two processors at once on one database file.
/// </para>
/// </summary>
public sealed class OutboxLeaseTests : IDisposable
{
    private const string OtherProcessor = "other-host:1:00000000000000000000000000000000";

    private readonly SqliteDatabase _db = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));

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
                    o.RegisterEvent<ShelfCreated>().RegisterEvent<ShelfRenamed>();
                    outbox?.Invoke(o);
                });
            },
            dispatchThroughRecorder: true,
            services => services.AddOutboxProcessor<LibraryContext>());

    /// <summary>One save per shelf, a millisecond apart, so the outbox holds them oldest first in this order.</summary>
    private async Task<List<ShelfId>> SaveShelvesAsync(TestHost host, params string[] names)
    {
        List<ShelfId> ids = [];
        foreach (var name in names)
        {
            var shelf = new Shelf(ShelfId.CreateUnique(), name, UserId.CreateUnique(), CatId.CreateUnique(), null);
            await host.InScopeAsync(async context =>
            {
                context.Shelves.Add(shelf);
                await context.SaveChangesAsync(Cancellation);
            });

            ids.Add(shelf.Id);
            _clock.Advance(TimeSpan.FromMilliseconds(1));
        }

        return ids;
    }

    private static Task<int> ProcessAsync(TestHost host, int batchSize = 100, CancellationToken? cancellationToken = null)
        => host.InScopeAsync((_, services) => services.GetRequiredService<OutboxProcessor<LibraryContext>>().ProcessPendingAsync(batchSize, cancellationToken ?? Cancellation));

    /// <summary>Writes another processor's claim into the row of <paramref name="eventId"/>, as its claim would.</summary>
    private void ClaimForAnotherProcessor(Guid eventId, DateTimeOffset until)
    {
        using var other = _db.CreateLibraryContext();
        other.Outbox
            .Where(message => message.Id == eventId)
            .ExecuteUpdate(set => set
                .SetProperty(message => message.LockedBy, OtherProcessor)
                .SetProperty(message => message.LockedUntil, until));
    }

    private OutboxMessage Row(string shelf)
    {
        using var check = _db.CreateLibraryContext();
        return check.Outbox.AsEnumerable().Single(m => m.EventName == "shelf.created" && m.Payload.Contains($"\"Name\":\"{shelf}\"", StringComparison.Ordinal));
    }

    private List<OutboxMessage> Rows()
    {
        using var check = _db.CreateLibraryContext();
        return [.. check.Outbox.AsNoTracking()];
    }

    [Fact]
    public void The_lease_lasts_five_minutes_and_refuses_a_duration_of_zero_or_longer_than_a_day()
    {
        var outbox = new OutboxOptions();

        outbox.LeaseDuration.Should().Be(TimeSpan.FromMinutes(5));

        ((Action)(() => outbox.LeaseDuration = TimeSpan.Zero)).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => outbox.LeaseDuration = TimeSpan.FromSeconds(-1))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => outbox.LeaseDuration = TimeSpan.FromDays(1) + TimeSpan.FromTicks(1))).Should().Throw<ArgumentOutOfRangeException>();

        outbox.LeaseDuration = TimeSpan.FromDays(1);
        outbox.LeaseDuration.Should().Be(TimeSpan.FromDays(1));
    }

    [Fact]
    public async Task While_a_message_is_delivered_its_row_holds_this_processors_claim_for_the_lease()
    {
        using var host = CreateHost();
        OutboxMessage? during = null;
        host.Recorder.OnEvent = (_, _, _) =>
        {
            using var check = _db.CreateLibraryContext();
            during = check.Outbox.AsNoTracking().Single();
            return Task.CompletedTask;
        };
        await SaveShelvesAsync(host, "Fiction");

        (await ProcessAsync(host)).Should().Be(1);

        during!.LockedUntil.Should().Be(_clock.GetUtcNow() + TimeSpan.FromMinutes(5));
        during.LockedBy.Should().MatchRegex($"^[^:]+:{Environment.ProcessId}:[0-9a-f]{{32}}$", "the claim names the machine and the process, and a Guid sets each batch apart");

        var row = Row("Fiction");
        row.ProcessedAt.Should().NotBeNull();
        row.LockedBy.Should().BeNull("the processed mark releases the row");
        row.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task A_failed_attempt_releases_the_row_until_its_NextAttemptAt()
    {
        using var host = CreateHost();
        host.Recorder.OnEvent = (_, _, _) => throw new InvalidOperationException("the handler is down");
        await SaveShelvesAsync(host, "Fiction");

        (await ProcessAsync(host)).Should().Be(0);

        var row = Row("Fiction");
        row.Attempts.Should().Be(1);
        row.LastError.Should().Contain("the handler is down");
        row.NextAttemptAt.Should().Be(_clock.GetUtcNow() + TimeSpan.FromSeconds(5));
        row.LockedBy.Should().BeNull("the failure mark releases the row too");
        row.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task A_message_another_processor_holds_is_skipped_until_its_lease_ends()
    {
        using var host = CreateHost();
        await SaveShelvesAsync(host, "Held", "Free");
        var held = Row("Held");
        ClaimForAnotherProcessor(held.Id, _clock.GetUtcNow() + TimeSpan.FromMinutes(1));

        (await ProcessAsync(host)).Should().Be(1);
        host.Recorder.OfType<ShelfCreated>().Select(e => e.Name).Should().Equal(["Free"], "the other processor holds the older row");
        Row("Held").LockedBy.Should().Be(OtherProcessor);

        _clock.Advance(TimeSpan.FromMinutes(1));

        (await ProcessAsync(host)).Should().Be(1, "a lease ends at its LockedUntil, and the row is claimed again");
        host.Recorder.OfType<ShelfCreated>().Select(e => e.Name).Should().Equal("Free", "Held");
        Row("Held").ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_mark_after_another_processor_took_the_message_over_changes_nothing()
    {
        using var host = CreateHost();
        host.Recorder.OnEvent = (_, domainEvent, _) =>
        {
            // This delivery ran past its lease, and another processor claimed the row in the meantime.
            ClaimForAnotherProcessor(domainEvent.EventId, _clock.GetUtcNow() + TimeSpan.FromMinutes(5));
            return Task.CompletedTask;
        };
        await SaveShelvesAsync(host, "Fiction");

        (await ProcessAsync(host)).Should().Be(0, "the other processor holds the message, and its delivery is the one recorded");

        var row = Row("Fiction");
        row.LockedBy.Should().Be(OtherProcessor, "the mark carried this claim in its WHERE clause, so it changed no row");
        row.ProcessedAt.Should().BeNull();
        row.Attempts.Should().Be(0);
        row.LastError.Should().BeNull("a lost lease is not a failed delivery");
    }

    [Fact]
    public async Task A_failure_after_another_processor_took_the_message_over_is_not_recorded_either()
    {
        using var host = CreateHost();
        host.Recorder.OnEvent = (_, domainEvent, _) =>
        {
            ClaimForAnotherProcessor(domainEvent.EventId, _clock.GetUtcNow() + TimeSpan.FromMinutes(5));
            throw new InvalidOperationException("the handler is down");
        };
        await SaveShelvesAsync(host, "Fiction");

        (await ProcessAsync(host)).Should().Be(0);

        var row = Row("Fiction");
        row.LockedBy.Should().Be(OtherProcessor);
        row.Attempts.Should().Be(0);
        row.LastError.Should().BeNull("the failure mark carries the claim too, and the other processor's delivery decides");
        row.NextAttemptAt.Should().BeNull();
    }

    [Fact]
    public async Task What_a_handler_left_on_the_processors_context_is_not_saved_when_the_lease_was_lost()
    {
        using var host = CreateHost();
        host.Recorder.OnEvent = async (services, domainEvent, ct) =>
        {
            var created = (ShelfCreated)domainEvent;
            var context = services.GetRequiredService<LibraryContext>();
            (await context.Shelves.SingleAsync(s => s.Id == created.ShelfId, ct)).Rename($"{created.Name}, handled");

            if (created.Name == "Lost")
            {
                ClaimForAnotherProcessor(domainEvent.EventId, _clock.GetUtcNow() + TimeSpan.FromMinutes(5));
            }
        };
        var shelves = await SaveShelvesAsync(host, "Lost", "Kept");

        (await ProcessAsync(host)).Should().Be(1);

        using var check = _db.CreateLibraryContext();
        check.Shelves.Single(s => s.Id == shelves[0]).Name.Should().Be("Lost", "the rename was in the save that found the lease gone, and the next mark does not carry it");
        check.Shelves.Single(s => s.Id == shelves[1]).Name.Should().Be("Kept, handled", "the next message's handler change commits with its mark as usual");
        check.Outbox.AsNoTracking().Where(m => m.EventName == "shelf.renamed").Should().ContainSingle("only the kept rename raised an event that reached the outbox");
    }

    [Fact]
    public async Task A_handler_that_saves_the_processors_context_after_a_takeover_fails_the_save_and_records_nothing()
    {
        using var host = CreateHost();
        host.Recorder.OnEvent = async (services, domainEvent, ct) =>
        {
            var created = (ShelfCreated)domainEvent;
            var context = services.GetRequiredService<LibraryContext>();
            (await context.Shelves.SingleAsync(s => s.Id == created.ShelfId, ct)).Rename("Handled");
            ClaimForAnotherProcessor(domainEvent.EventId, _clock.GetUtcNow() + TimeSpan.FromMinutes(5));

            // The context also holds the row's incremented Attempts, and saves it with the claim in its WHERE clause.
            await context.SaveChangesAsync(ct);
        };
        var shelves = await SaveShelvesAsync(host, "Fiction");

        (await ProcessAsync(host)).Should().Be(0);

        var row = Row("Fiction");
        row.LockedBy.Should().Be(OtherProcessor);
        row.LastError.Should().BeNull("the handler's save failed on the lost lease, which is not a failed delivery");
        using var check = _db.CreateLibraryContext();
        check.Shelves.Single(s => s.Id == shelves[0]).Name.Should().Be("Fiction");
    }

    [Fact]
    public async Task A_message_whose_lease_ended_without_a_takeover_is_still_marked()
    {
        using var host = CreateHost();
        host.Recorder.OnEvent = (_, _, _) =>
        {
            _clock.Advance(TimeSpan.FromMinutes(6));
            return Task.CompletedTask;
        };
        await SaveShelvesAsync(host, "Slow");

        (await ProcessAsync(host)).Should().Be(1, "the mark checks who holds the row, not whether the lease ended");

        var row = Row("Slow");
        row.ProcessedAt.Should().NotBeNull();
        row.LockedBy.Should().BeNull();
    }

    [Fact]
    public async Task A_batch_stops_when_less_than_half_the_lease_is_left_and_releases_the_rest()
    {
        using var host = CreateHost(outbox => outbox.LeaseDuration = TimeSpan.FromMinutes(10));
        host.Recorder.OnEvent = (_, _, _) =>
        {
            _clock.Advance(TimeSpan.FromMinutes(3));
            return Task.CompletedTask;
        };
        await SaveShelvesAsync(host, "first", "second", "third", "fourth");

        // first starts at 0 and second at 3 minutes; at 6 minutes less than half of the 10 minute lease is left.
        (await ProcessAsync(host)).Should().Be(2);

        host.Recorder.OfType<ShelfCreated>().Select(e => e.Name).Should().Equal("first", "second");
        Row("third").LockedBy.Should().BeNull("the rows the batch did not reach are released");
        Row("fourth").LockedUntil.Should().BeNull();

        (await ProcessAsync(host)).Should().Be(2, "the next batch claims them at once, without waiting for the lease to end");
        host.Recorder.OfType<ShelfCreated>().Select(e => e.Name).Should().Equal("first", "second", "third", "fourth");
    }

    [Fact]
    public async Task A_cancelled_batch_releases_the_messages_it_did_not_finish()
    {
        using var host = CreateHost();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        host.Recorder.OnEvent = (_, _, _) =>
        {
            stop.Cancel();
            return Task.CompletedTask;
        };
        await SaveShelvesAsync(host, "first", "second", "third");

        var process = () => ProcessAsync(host, cancellationToken: stop.Token);

        await process.Should().ThrowAsync<OperationCanceledException>();
        Rows().Should().OnlyContain(row => row.LockedBy == null && row.LockedUntil == null, "a stopped batch hands its rows back for the next processor");

        host.Recorder.OnEvent = null;
        (await ProcessAsync(host)).Should().BeGreaterThanOrEqualTo(2, "the rows are claimed again at once");
        Rows().Should().OnlyContain(row => row.ProcessedAt != null);
    }

    /// <summary>
    /// Two processors, each in a host of its own with its own connections, drain one outbox at the same time.
    /// Without the claim both read the same batches, so both deliver most messages. SQLite runs one writer at a
    /// time, so this proves the claim and the marks end to end; the provider tests prove the same on servers
    /// that run writers side by side.
    /// </summary>
    [Fact]
    public async Task Two_processors_on_one_database_deliver_every_message_exactly_once()
    {
        const int Messages = 120;
        var file = Path.Combine(Path.GetTempPath(), $"outbox-lease-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = file, Pooling = true, DefaultTimeout = 60 }.ToString();
        var deliveries = new ConcurrentDictionary<Guid, int>();

        try
        {
            using (var setup = new SqliteConnection(connectionString))
            {
                setup.Open();
                using var wal = setup.CreateCommand();
                wal.CommandText = "PRAGMA journal_mode=WAL;";
                wal.ExecuteNonQuery();
            }

            using var first = new FileHost(connectionString, new CountingSink(deliveries));
            using var second = new FileHost(connectionString, new CountingSink(deliveries));

            await first.InScopeAsync(async context =>
            {
                for (var i = 0; i < Messages; i++)
                {
                    context.Shelves.Add(new Shelf(ShelfId.CreateUnique(), $"Shelf {i}", UserId.CreateUnique(), CatId.CreateUnique(), null));
                }

                await context.SaveChangesAsync(Cancellation);
                return 0;
            });

            await Task.WhenAll(Task.Run(() => first.DrainAsync(batchSize: 10), Cancellation), Task.Run(() => second.DrainAsync(batchSize: 10), Cancellation));

            deliveries.Should().HaveCount(Messages, "every message is delivered");
            deliveries.Values.Should().OnlyContain(count => count == 1, "no message is delivered by both processors");

            var rows = await first.InScopeAsync(context => context.Outbox.AsNoTracking().ToListAsync(Cancellation));
            rows.Should().OnlyContain(row => row.ProcessedAt != null && row.Attempts == 1 && row.LockedBy == null);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { file, file + "-wal", file + "-shm" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    /// <summary>Counts the deliveries of every message.</summary>
    private sealed class CountingSink(ConcurrentDictionary<Guid, int> deliveries) : IIntegrationEventSink
    {
        public async Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default)
        {
            deliveries.AddOrUpdate(message.MessageId, 1, (_, count) => count + 1);

            // Long enough for the other processor to claim and deliver in between.
            await Task.Delay(1, cancellationToken);
        }
    }

    /// <summary>A host over a database file, so each scope's context opens a connection of its own.</summary>
    private sealed class FileHost : IDisposable
    {
        private readonly ServiceProvider _provider;

        public FileHost(string connectionString, IIntegrationEventSink sink)
        {
            var services = new ServiceCollection();
            services.AddDDDToolkitEntityFramework(options => options.UseOutbox(outbox => outbox.RegisterEvent<ShelfCreated>().SendTo(sink)));
            services.AddDbContext<LibraryContext>((provider, options) => options.UseSqlite(connectionString).UseDDDToolkit(provider));
            services.AddOutboxProcessor<LibraryContext>();
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

            using var scope = _provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<LibraryContext>().Database.EnsureCreated();
        }

        public async Task<T> InScopeAsync<T>(Func<LibraryContext, Task<T>> action)
        {
            using var scope = _provider.CreateScope();
            return await action(scope.ServiceProvider.GetRequiredService<LibraryContext>());
        }

        public async Task DrainAsync(int batchSize)
        {
            int delivered;
            do
            {
                using var scope = _provider.CreateScope();
                delivered = await scope.ServiceProvider.GetRequiredService<OutboxProcessor<LibraryContext>>().ProcessPendingAsync(batchSize, Cancellation);
            }
            while (delivered > 0);
        }

        public void Dispose() => _provider.Dispose();
    }
}
