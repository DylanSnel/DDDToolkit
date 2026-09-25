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
    private Predicate<IDomainEvent> _refuses = _ => false;

    private ProviderHost CreateOutboxHost()
        => new(
            Database,
            options =>
            {
                options.TimeProvider = _clock;
                options.DispatchInProcess((_, events, _) =>
                {
                    if (events.Any(e => _refuses(e)))
                    {
                        throw new InvalidOperationException("refused");
                    }

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
                // is under test, so they are recorded and go no further.
                options.DispatchInProcess((_, events, _) =>
                {
                    _dispatched.AddRange(events);
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
    public async Task A_failed_message_waits_for_its_NextAttemptAt_on_this_providers_timestamp_column()
    {
        SkipIfUnavailable();

        // The processor compares NextAttemptAt with the current time in SQL, so like the ordering above it
        // has to hold on each provider's own column. The clock carries an offset: the comparison's
        // parameter has to be normalized the way a written value is, or Npgsql refuses it.
        using var host = CreateOutboxHost();
        _refuses = e => e is ShelfCreated { Name: "broken" };
        _clock.Set(new DateTimeOffset(2026, 9, 13, 14, 0, 0, TimeSpan.FromHours(2)));

        foreach (var name in new[] { "broken", "healthy" })
        {
            await host.InScopeAsync(async context =>
            {
                context.Shelves.Add(NewShelf(name));
                await context.SaveChangesAsync(Cancellation);
            });
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        Task<int> ProcessAsync() => host.InScopeAsync((_, services) => services
            .GetRequiredService<OutboxProcessor<ProviderContext>>()
            .ProcessPendingAsync(batchSize: 1, Cancellation));

        (await ProcessAsync()).Should().Be(0, "the oldest row fails");
        (await ProcessAsync()).Should().Be(1, "and then waits, so the next batch of one reaches the row behind it");
        _dispatched.OfType<ShelfCreated>().Select(e => e.Name).Should().Equal("healthy");

        await using (var check = Database.CreateContext())
        {
            var waiting = await check.Outbox.SingleAsync(m => m.ProcessedAt == null, Cancellation);
            waiting.Attempts.Should().Be(1);
            waiting.NextAttemptAt.Should().Be(_clock.GetUtcNow() + TimeSpan.FromSeconds(5));
            waiting.NextAttemptAt!.Value.Offset.Should().Be(TimeSpan.Zero);
        }

        _refuses = _ => false;
        _clock.Advance(TimeSpan.FromSeconds(5) - TimeSpan.FromMilliseconds(1));
        (await ProcessAsync()).Should().Be(0, "a millisecond early is still early");

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        (await ProcessAsync()).Should().Be(1, "at NextAttemptAt the row is due");
        _dispatched.OfType<ShelfCreated>().Select(e => e.Name).Should().Equal("healthy", "broken");
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
}
