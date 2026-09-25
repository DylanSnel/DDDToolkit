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
/// <see cref="OutboxOptions.DeliverInTransaction"/>: putting the delivery and the processed mark in one
/// transaction, for a sink that writes to the same database as the outbox.
/// <para>
/// The pgmq tests show what it buys against a real Postgres. These cover the option itself, on SQLite,
/// so the behaviour is pinned down wherever Docker is not.
/// </para>
/// </summary>
public sealed class OutboxTransactionTests : IDisposable
{
    private readonly SqliteDatabase _db = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));

    public void Dispose() => _db.Dispose();

    private TestHost CreateHost(bool inTransaction, Action<OutboxOptions> sinks)
        => new(
            _db,
            options =>
            {
                options.TimeProvider = _clock;
                options.UseOutbox(outbox =>
                {
                    outbox.RegisterEvent<ShelfCreated>();
                    outbox.DeliverInTransaction = inTransaction;
                    sinks(outbox);
                });
            },
            dispatchThroughRecorder: false,
            services => services.AddOutboxProcessor<LibraryContext>());

    private async Task SaveShelfAsync(TestHost host)
        => await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(new Shelf(ShelfId.CreateUnique(), "Fiction", UserId.CreateUnique(), CatId.CreateUnique(), null));
            await context.SaveChangesAsync();
        });

    private Task<int> ProcessAsync(TestHost host)
        => host.InScopeAsync((_, services) => services.GetRequiredService<OutboxProcessor<LibraryContext>>().ProcessPendingAsync());

    private OutboxMessage Row()
    {
        using var check = _db.CreateLibraryContext();
        return check.Outbox.Single();
    }

    [Fact]
    public async Task A_message_delivered_in_a_transaction_is_marked_processed_as_usual()
    {
        var sink = new RecordingSink();
        using var host = CreateHost(inTransaction: true, outbox => outbox.SendTo(sink));
        await SaveShelfAsync(host);

        var processed = await ProcessAsync(host);

        processed.Should().Be(1);
        sink.Messages.Should().ContainSingle();
        Row().ProcessedAt.Should().Be(_clock.GetUtcNow());
    }

    [Fact]
    public async Task A_failure_still_records_the_attempt_and_the_error_after_the_rollback()
    {
        var sink = new RecordingSink { Refuse = true };
        using var host = CreateHost(inTransaction: true, outbox => outbox.SendTo(sink));
        await SaveShelfAsync(host);

        var processed = await ProcessAsync(host);

        processed.Should().Be(0);

        // The attempt is rolled back; the bookkeeping about the attempt is not, or a poison message
        // would be retried for ever with nothing to show for it.
        var row = Row();
        row.ProcessedAt.Should().BeNull();
        row.Attempts.Should().Be(1);
        row.LastError.Should().Contain("is down");
        row.NextAttemptAt.Should().Be(_clock.GetUtcNow() + TimeSpan.FromSeconds(5), "the wait is bookkeeping too");
    }

    [Fact]
    public async Task What_a_sink_wrote_through_the_same_context_is_rolled_back_when_a_later_sink_fails()
    {
        var broken = new SecondRecordingSink { Refuse = true };
        using var host = CreateHost(inTransaction: true, outbox => outbox.SendTo<WritingSink>().SendTo(broken));
        await SaveShelfAsync(host);

        await ProcessAsync(host);

        using var check = _db.CreateLibraryContext();
        check.People.Should().BeEmpty("the whole attempt was one transaction, so the first sink's write went with it");
    }

    [Fact]
    public async Task A_sink_that_saved_through_the_same_context_does_not_take_the_attempt_with_it()
    {
        // The sink's save wrote the incremented Attempts inside the transaction, and the rollback took it
        // back while the change tracker still counted it as written. Unless the processor writes it
        // again the count never moves: the wait never grows and MaxAttempts never stops the message.
        var broken = new SecondRecordingSink { Refuse = true };
        using var host = CreateHost(inTransaction: true, outbox => outbox.SendTo<WritingSink>().SendTo(broken));
        await SaveShelfAsync(host);

        await ProcessAsync(host);
        _clock.Advance(TimeSpan.FromSeconds(5));
        await ProcessAsync(host);

        var row = Row();
        row.Attempts.Should().Be(2);
        row.NextAttemptAt.Should().Be(_clock.GetUtcNow() + TimeSpan.FromSeconds(25), "the second failure waits longer than the first");
        row.LastError.Should().Contain("is down");
    }

    [Fact]
    public async Task Without_the_option_the_same_write_survives_the_failure()
    {
        var broken = new SecondRecordingSink { Refuse = true };
        using var host = CreateHost(inTransaction: false, outbox => outbox.SendTo<WritingSink>().SendTo(broken));
        await SaveShelfAsync(host);

        await ProcessAsync(host);

        using var check = _db.CreateLibraryContext();
        check.People.Should().ContainSingle("each sink commits for itself, which is why a retry can deliver twice");
    }

    /// <summary>A sink that writes to the same database, which is the only kind the option is for.</summary>
    private sealed class WritingSink(LibraryContext context) : IIntegrationEventSink
    {
        public async Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default)
        {
            context.People.Add(new Person(MemberId.CreateUnique(), new PersonName("Ada", "Lovelace"), null, new ValidDateOfBirth(new DateOnly(1815, 12, 10))));
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}

