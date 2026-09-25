using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;
using DDDToolkit.EntityFramework.Storage;
using DDDToolkit.EntityFramework.Tests.Domain;
using DDDToolkit.EntityFramework.Tests.Domain.Events;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Providers.Tests.Providers;

/// <summary>
/// The outbox and the inbox on a real server. Two things only a real server can answer: whether a
/// rolled back transaction really takes the outbox row with it, and whether the processor's
/// "oldest first" ordering really holds on this provider's own timestamp column.
/// <para>
/// The second one is why the timestamp column is allowed to differ per provider at all. Ordering is
/// the only thing the outbox needs from that column, so it is the thing that has to be proven on each
/// provider rather than assumed from SQLite.
/// </para>
/// </summary>
public abstract class ProviderMessagingTests(ProviderFixture fixture) : ProviderTestBase(fixture)
{
    private const string Consumer = "billing.shelf-projector";

    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly List<IDomainEvent> _dispatched = [];

    private ProviderHost CreateOutboxHost()
        => new(
            Database,
            options =>
            {
                options.TimeProvider = _clock;
                options.DispatchInProcess((_, events, _) =>
                {
                    _dispatched.AddRange(events);
                    return Task.CompletedTask;
                });
                options.UseOutbox(outbox => outbox
                    .RegisterEvent<ShelfCreated>()
                    .RegisterEvent<ShelfRenamed>()
                    .RegisterEvent<BookAdded>());
            },
            services => services.AddOutboxProcessor<ProviderContext>());

    private ProviderHost CreateInboxHost()
        => new(
            Database,
            options =>
            {
                options.TimeProvider = _clock;

                // The handler's effect happens to be an aggregate that raises events. The inbox is what
                // is under test, so they are recorded and go no further. Locked, because the race tests
                // save from two copies at once.
                options.DispatchInProcess((_, events, _) =>
                {
                    lock (_dispatched)
                    {
                        _dispatched.AddRange(events);
                    }

                    return Task.CompletedTask;
                });
            },
            services => services.AddDomainEventInbox<ProviderContext>());

    private static Shelf NewShelf(string name)
        => new(ShelfId.CreateUnique(), name, UserId.CreateUnique(), CatId.CreateUnique(), null);

    [Fact]
    public async Task Save_writes_outbox_rows_and_the_processor_delivers_them_oldest_first()
    {
        SkipIfUnavailable();

        using var host = CreateOutboxHost();

        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(NewShelf("first"));
            await context.SaveChangesAsync(Cancellation);
        });

        // A later save: the processor orders by CreatedAt, which is this provider's own instant column.
        _clock.Advance(TimeSpan.FromMinutes(5));

        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(NewShelf("second"));
            await context.SaveChangesAsync(Cancellation);
        });

        _dispatched.Should().BeEmpty("outbox mode does not dispatch at save time");
        (await Database.CountRowsAsync(DomainEventStorage.DefaultOutboxTableName, DomainEventStorage.DefaultSchema, Cancellation)).Should().Be(2);

        var processed = await host.InScopeAsync((_, services) => services
            .GetRequiredService<OutboxProcessor<ProviderContext>>()
            .ProcessPendingAsync(cancellationToken: Cancellation));

        processed.Should().Be(2);
        _dispatched.OfType<ShelfCreated>().Select(e => e.Name).Should().Equal("first", "second");

        await using var check = Database.CreateContext();
        var rows = await check.Outbox.OrderBy(m => m.CreatedAt).ToListAsync(Cancellation);
        rows.Should().AllSatisfy(row =>
        {
            row.ProcessedAt.Should().NotBeNull();
            row.LastError.Should().BeNull();
            row.Attempts.Should().Be(1);
        });

        // The timestamps survive the round trip through the timestamp column as UTC instants.
        rows[0].CreatedAt.Should().Be(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        rows[1].CreatedAt.Should().Be(new DateTimeOffset(2026, 9, 13, 12, 5, 0, TimeSpan.Zero));
        rows[0].CreatedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task The_processor_reads_oldest_first_however_the_rows_were_inserted()
    {
        SkipIfUnavailable();

        // The previous test writes in the order it wants them back, so a column that did not sort at all
        // would still pass it. This one inserts newest first, crosses a year boundary and a day boundary,
        // and mixes the offset the caller wrote in. Only a column that really orders as an instant, and
        // a write path that really normalizes to UTC, answers this correctly.
        using var host = CreateOutboxHost();

        var start = new DateTimeOffset(2025, 12, 31, 23, 30, 0, TimeSpan.Zero);
        var written = new[]
        {
            ("fourth", start.AddHours(3).ToOffset(TimeSpan.FromHours(5))),
            ("second", start.AddMinutes(20)),
            ("third", start.AddHours(1).ToOffset(TimeSpan.FromHours(-8))),
            ("first", start),
        };

        foreach (var (name, at) in written)
        {
            _clock.Set(at);

            await host.InScopeAsync(async context =>
            {
                context.Shelves.Add(NewShelf(name));
                await context.SaveChangesAsync(Cancellation);
            });
        }

        var processed = await host.InScopeAsync((_, services) => services
            .GetRequiredService<OutboxProcessor<ProviderContext>>()
            .ProcessPendingAsync(cancellationToken: Cancellation));

        processed.Should().Be(4);
        _dispatched.OfType<ShelfCreated>().Select(e => e.Name).Should().Equal("first", "second", "third", "fourth");

        await using var check = Database.CreateContext();
        var rows = await check.Outbox.OrderBy(m => m.CreatedAt).ToListAsync(Cancellation);

        rows.Select(r => r.CreatedAt).Should().BeInAscendingOrder().And.AllSatisfy(at => at.Offset.Should().Be(TimeSpan.Zero));
        rows[0].CreatedAt.Should().Be(start, "the row written last is the oldest instant, whatever offset it arrived in");
        rows[3].CreatedAt.Should().Be(start.AddHours(3), "and it crossed into the next year on the way");
    }

    [Fact]
    public async Task A_rolled_back_transaction_takes_the_outbox_row_with_it()
    {
        SkipIfUnavailable();

        using var host = CreateOutboxHost();

        await host.InScopeAsync(async context =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(Cancellation);
            context.Shelves.Add(NewShelf("never committed"));
            await context.SaveChangesAsync(Cancellation);
            await transaction.RollbackAsync(Cancellation);
        });

        (await Database.CountRowsAsync(DomainEventStorage.DefaultOutboxTableName, DomainEventStorage.DefaultSchema, Cancellation))
            .Should().Be(0, "the event and the aggregate are one unit of work");
        (await Database.CountRowsAsync("Shelves", schema: null, Cancellation)).Should().Be(0);
    }

    [Fact]
    public async Task Inbox_runs_a_handler_once_per_message_and_consumer()
    {
        SkipIfUnavailable();

        using var host = CreateInboxHost();
        var messageId = Guid.CreateVersion7();
        var runs = 0;

        Task<bool> DeliverAsync(string consumer) => host.InScopeAsync((context, services) => services
            .GetRequiredService<DomainEventInbox<ProviderContext>>()
            .ExecuteOnceAsync(messageId, consumer, _ =>
            {
                runs++;
                context.Shelves.Add(NewShelf($"projected {runs}"));
                return Task.CompletedTask;
            }, Cancellation));

        (await DeliverAsync(Consumer)).Should().BeTrue();
        (await DeliverAsync(Consumer)).Should().BeFalse("the row for this message and this consumer already exists");
        (await DeliverAsync("search.shelf-indexer")).Should().BeTrue("the key is the pair, so another consumer runs too");

        runs.Should().Be(2);
        (await Database.CountRowsAsync("Shelves", schema: null, Cancellation)).Should().Be(2);

        await using var check = Database.CreateContext();
        var rows = await check.Inbox.ToListAsync(Cancellation);
        rows.Should().HaveCount(2);
        rows.Select(r => r.Consumer).Should().BeEquivalentTo(Consumer, "search.shelf-indexer");
        rows.Should().AllSatisfy(row => row.ProcessedAt.Should().Be(_clock.GetUtcNow()));
    }

    [Fact]
    public async Task Two_copies_delivered_at_once_apply_the_message_once_and_neither_fails()
    {
        SkipIfUnavailable();

        using var host = CreateInboxHost();
        var messageId = Guid.CreateVersion7();
        var bothInside = new Rendezvous(2);

        // Each copy waits inside its own transaction until the other is there too, so both have already
        // found no inbox row. Then both save, and the server decides which one was first.
        Task<bool> DeliverAsync() => host.InScopeAsync((context, services) => services
            .GetRequiredService<DomainEventInbox<ProviderContext>>()
            .ExecuteOnceAsync(messageId, Consumer, async token =>
            {
                await bothInside.ArriveAsync(token);
                context.Shelves.Add(NewShelf("projected"));
            }, Cancellation));

        var results = await Task.WhenAll(DeliverAsync(), DeliverAsync());

        results.Should().BeEquivalentTo([true, false], "one copy applied the message and the other is a repeat, not a failure");
        (await Database.CountRowsAsync("Shelves", schema: null, Cancellation)).Should().Be(1);
        (await Database.CountRowsAsync(DomainEventStorage.DefaultInboxTableName, DomainEventStorage.DefaultSchema, Cancellation)).Should().Be(1);
    }

    [Fact]
    public async Task Two_copies_changing_the_same_aggregate_at_once_apply_the_message_once()
    {
        SkipIfUnavailable();

        using var host = CreateInboxHost();
        var messageId = Guid.CreateVersion7();
        var shelf = NewShelf("unnamed");
        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync(Cancellation);
        });

        var bothInside = new Rendezvous(2);

        // The loser may fail on the aggregate's version rather than on the inbox row, depending on which
        // the server reaches first. Either way it lost to the other copy of the same message.
        Task<bool> DeliverAsync(string name) => host.InScopeAsync((context, services) => services
            .GetRequiredService<DomainEventInbox<ProviderContext>>()
            .ExecuteOnceAsync(messageId, Consumer, async token =>
            {
                var loaded = await context.Shelves.SingleAsync(s => s.Id == shelf.Id, token);
                await bothInside.ArriveAsync(token);
                loaded.Rename(name);
            }, Cancellation));

        var results = await Task.WhenAll(DeliverAsync("first"), DeliverAsync("second"));

        results.Should().BeEquivalentTo([true, false]);

        await using var check = Database.CreateContext();
        var stored = await check.Shelves.SingleAsync(s => s.Id == shelf.Id, Cancellation);
        stored.Name.Should().BeOneOf("first", "second");
        stored.Version.Should().Be(shelf.Version + 1, "the aggregate was changed once");
        (await check.Inbox.CountAsync(Cancellation)).Should().Be(1);
    }

    [Fact]
    public async Task A_handler_that_throws_leaves_neither_its_effect_nor_the_inbox_marker()
    {
        SkipIfUnavailable();

        using var host = CreateInboxHost();
        var messageId = Guid.CreateVersion7();

        var act = () => host.InScopeAsync((context, services) => services
            .GetRequiredService<DomainEventInbox<ProviderContext>>()
            .ExecuteOnceAsync(messageId, Consumer, _ =>
            {
                context.Shelves.Add(NewShelf("half applied"));
                return Task.FromException(new InvalidOperationException("handler failed"));
            }, Cancellation));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("handler failed");

        (await Database.CountRowsAsync("Shelves", schema: null, Cancellation)).Should().Be(0);
        (await Database.CountRowsAsync(DomainEventStorage.DefaultInboxTableName, DomainEventStorage.DefaultSchema, Cancellation)).Should().Be(0);

        // And the message is still deliverable, because nothing recorded that it was handled.
        var applied = await host.InScopeAsync((context, services) => services
            .GetRequiredService<DomainEventInbox<ProviderContext>>()
            .ExecuteOnceAsync(messageId, Consumer, _ =>
            {
                context.Shelves.Add(NewShelf("retried"));
                return Task.CompletedTask;
            }, Cancellation));

        applied.Should().BeTrue();
        (await Database.CountRowsAsync("Shelves", schema: null, Cancellation)).Should().Be(1);
    }

    [Fact]
    public async Task Retention_deletes_expired_rows_batch_by_batch_on_this_providers_timestamp_column()
    {
        SkipIfUnavailable();

        // Two things only a server answers: whether a batched ExecuteDelete translates here, over the
        // inbox's composite key too, and whether "older than" compares right on this provider's own
        // timestamp column.
        using var host = new ProviderHost(
            Database,
            options => options.TimeProvider = _clock,
            services => services.AddDomainEventRetention<ProviderContext>(retention =>
            {
                retention.KeepOutboxFor = TimeSpan.FromDays(7);
                retention.KeepInboxFor = TimeSpan.FromDays(30);
                retention.BatchSize = 2;
            }));

        var now = _clock.GetUtcNow();
        await using (var seed = Database.CreateContext())
        {
            foreach (var age in new[] { 8, 9, 10, 6 })
            {
                seed.Outbox.Add(Delivered(now.AddDays(-age)));
            }

            var waiting = Delivered(now.AddDays(-40));
            waiting.ProcessedAt = null;
            seed.Outbox.Add(waiting);

            foreach (var age in new[] { 31, 32, 33, 29 })
            {
                seed.Inbox.Add(new InboxMessage { MessageId = Guid.CreateVersion7(), Consumer = Consumer, ProcessedAt = now.AddDays(-age) });
            }

            await seed.SaveChangesAsync(Cancellation);
        }

        var result = await host.InScopeAsync((_, services) => services
            .GetRequiredService<DomainEventRetention<ProviderContext>>()
            .DeleteExpiredAsync(Cancellation));

        result.Should().Be(new DomainEventRetentionResult(OutboxMessages: 3, InboxMessages: 3));

        await using var check = Database.CreateContext();
        (await check.Outbox.CountAsync(Cancellation)).Should().Be(2, "the recent delivered row and the one never delivered");
        (await check.Outbox.CountAsync(m => m.ProcessedAt == null, Cancellation)).Should().Be(1);
        (await check.Inbox.SingleAsync(Cancellation)).ProcessedAt.Should().Be(now.AddDays(-29));
    }

    private static OutboxMessage Delivered(DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(),
        EventName = "shelf.created",
        Payload = "{}",
        OccurredAt = at,
        CreatedAt = at,
        ProcessedAt = at,
        Attempts = 1,
    };

    /// <summary>Holds everyone who arrives until <paramref name="count"/> have.</summary>
    private sealed class Rendezvous(int count)
    {
        private readonly TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public Task ArriveAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrived) == count)
            {
                _all.TrySetResult();
            }

            return _all.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
    }
}
