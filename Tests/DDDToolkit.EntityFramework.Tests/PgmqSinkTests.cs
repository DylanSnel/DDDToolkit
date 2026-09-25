using System.Text.Json;
using DDDToolkit.BaseTypes;
using DDDToolkit.Messaging.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The pgmq sink against a real Postgres. What is worth proving here is the one thing pgmq has that a
/// broker does not: the enqueue obeys the transaction it was called in.
/// <para>
/// Every test skips itself when Docker is not available, because there is no honest way to fake a
/// transactional queue. Except in CI, where <see cref="RequiredContainers"/> turns that skip into a
/// failure: a run that never started Postgres must not report that pgmq works.
/// </para>
/// </summary>
public sealed class PgmqSinkTests : IAsyncLifetime
{
    private const string Queue = "integration_events";

    private PgmqDatabase? _database;

    public async ValueTask InitializeAsync() => _database = await PgmqDatabase.StartAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    /// <summary>
    /// The running database. When there was no Docker to start one this skips, or fails where
    /// <see cref="RequiredContainers"/> says a container was not optional.
    /// </summary>
    private PgmqDatabase Database
    {
        get
        {
            RequiredContainers.EnforceOrSkip(
                available: _database is not null,
                RequiredContainers.Required,
                "pgmq on PostgreSQL",
                $"No Docker here, so '{PgmqDatabase.Image}' could not be started. The pgmq behaviour is not covered on this machine.");

            return _database!;
        }
    }

    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static IntegrationEventMessage Message(string name = "library.shelf-opened", int version = 3, string? aggregateId = null)
        => new()
        {
            MessageId = Guid.CreateVersion7(),
            Name = name,
            Version = version,
            Payload = JsonSerializer.Serialize(new { ShelfId = "SHELF_1", DisplayName = "Fiction" }),
            OccurredAt = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero),
            AggregateType = "Shelf",
            AggregateId = aggregateId ?? "SHELF_1",
        };

    [Fact]
    public async Task A_message_goes_on_the_queue_with_the_envelope_in_its_headers()
    {
        var database = Database;
        await using var connection = await database.OpenAsync(Cancellation);
        var sink = new PgmqSink(NpgsqlDataSource.Create(database.ConnectionString), new PgmqSinkOptions());
        var message = Message();

        await sink.SendAsync(message, Cancellation);

        var queued = await PgmqQueue.ReadAsync(connection, null, Queue, count: 10, cancellationToken: Cancellation);
        var row = queued.Should().ContainSingle().Subject;
        row.Body.Should().Contain("Fiction");
        row.ReadCount.Should().Be(1);

        var headers = JsonDocument.Parse(row.Headers!).RootElement;
        headers.GetProperty("messageId").GetString().Should().Be(message.MessageId.ToString());
        headers.GetProperty("name").GetString().Should().Be("library.shelf-opened");
        headers.GetProperty("version").GetString().Should().Be("3");
        headers.GetProperty("aggregateId").GetString().Should().Be("SHELF_1");
    }

    [Fact]
    public async Task The_enqueue_obeys_the_transaction_it_was_called_in()
    {
        var database = Database;
        await using var context = database.CreateContext();
        await context.Database.EnsureCreatedAsync(Cancellation);
        var sink = new PgmqSink<PgmqContext>(context, new PgmqSinkOptions().UseQueue("rollback_q"));

        // A transaction that changes its mind. A broker would already have sent the message.
        await using (var transaction = await context.Database.BeginTransactionAsync(Cancellation))
        {
            await sink.SendAsync(Message(aggregateId: "rolled-back"), Cancellation);
            await transaction.RollbackAsync(Cancellation);
        }

        (await ReadAsync(database, "rollback_q")).Should().BeEmpty("the queue is a table, so a rolled back enqueue leaves nothing behind");

        await using (var transaction = await context.Database.BeginTransactionAsync(Cancellation))
        {
            await sink.SendAsync(Message(aggregateId: "committed"), Cancellation);
            await transaction.CommitAsync(Cancellation);
        }

        var queued = await ReadAsync(database, "rollback_q");
        queued.Should().ContainSingle().Which.Headers.Should().Contain("committed");
    }

    [Fact]
    public async Task A_database_without_the_extension_says_so_and_says_how_to_add_it()
    {
        var database = Database;
        var plain = await database.CreateDatabaseWithoutPgmqAsync("no_pgmq", Cancellation);
        var sink = new PgmqSink(NpgsqlDataSource.Create(plain), new PgmqSinkOptions());

        var send = () => sink.SendAsync(Message(), Cancellation);

        (await send.Should().ThrowAsync<PgmqNotInstalledException>())
            .WithMessage("*no_pgmq*")
            .WithMessage("*CREATE EXTENSION IF NOT EXISTS pgmq*");
    }

    [Fact]
    public async Task A_missing_queue_is_created_once_and_then_reused()
    {
        var database = Database;
        var sink = new PgmqSink(NpgsqlDataSource.Create(database.ConnectionString), new PgmqSinkOptions().UseQueue("made_on_demand"));

        await sink.SendAsync(Message(), Cancellation);
        await sink.SendAsync(Message(), Cancellation);

        (await ReadAsync(database, "made_on_demand")).Should().HaveCount(2);
    }

    [Fact]
    public async Task Turning_creation_off_lets_a_missing_queue_fail_instead_of_being_papered_over()
    {
        var database = Database;
        var sink = new PgmqSink(
            NpgsqlDataSource.Create(database.ConnectionString),
            new PgmqSinkOptions { CreateQueueIfMissing = false }.UseQueue("never_created"));

        var send = () => sink.SendAsync(Message(), Cancellation);

        // Postgres's own error, not ours: the extension is there, the queue is not, and that is a
        // deployment mistake rather than something this package should guess at.
        await send.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task Reading_hides_a_message_until_it_is_archived()
    {
        var database = Database;
        await using var connection = await database.OpenAsync(Cancellation);
        await PgmqQueue.CreateAsync(connection, null, "visibility_q", Cancellation);
        await PgmqQueue.SendAsync(connection, null, "visibility_q", """{"a":1}""", cancellationToken: Cancellation);

        var first = await PgmqQueue.ReadAsync(connection, null, "visibility_q", visibilityTimeout: 30, cancellationToken: Cancellation);
        var second = await PgmqQueue.ReadAsync(connection, null, "visibility_q", visibilityTimeout: 30, cancellationToken: Cancellation);

        first.Should().ContainSingle();
        second.Should().BeEmpty("a read message is invisible for the timeout, which is what stops two consumers doing the same work");

        var archived = await PgmqQueue.ArchiveAsync(connection, null, "visibility_q", first[0].MessageId, Cancellation);
        var again = await PgmqQueue.ArchiveAsync(connection, null, "visibility_q", first[0].MessageId, Cancellation);

        archived.Should().BeTrue();
        again.Should().BeFalse("archiving something already archived is a no-op, not an error");
    }

    [Fact]
    public async Task A_context_that_is_not_on_postgres_says_so_rather_than_failing_in_the_driver()
    {
        using var sqlite = new SqliteDatabase();
        await using var context = sqlite.CreateLibraryContext();
        var sink = new PgmqSink<Infrastructure.LibraryContext>(context, new PgmqSinkOptions());

        var send = () => sink.SendAsync(Message());

        (await send.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*LibraryContext*")
            .WithMessage("*NpgsqlDataSource*");
    }

    [Fact]
    public async Task Behind_the_outbox_the_enqueue_and_the_processed_mark_are_one_commit()
    {
        var database = Database;
        await using var context = database.CreateContext();
        await context.Database.EnsureCreatedAsync(Cancellation);

        var broken = new RecordingSink { Refuse = true };
        var clock = new ManualClock(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        var options = new Options.DDDEntityFrameworkOptions { TimeProvider = clock };
        options.UseOutbox(outbox =>
        {
            outbox.RegisterEvent<Domain.Events.ShelfCreated>();
            outbox.DeliverInTransaction = true;
            // pgmq first, so the failure behind it is what decides the outcome of the enqueue.
            outbox.SendTo(new PgmqSink<PgmqContext>(context, new PgmqSinkOptions().UseQueue("outbox_q")));
            outbox.SendTo(broken);
        });

        var id = await WriteRowAsync(context);
        var processor = new Outbox.OutboxProcessor<PgmqContext>(context, new ServiceProviderStub(), options);

        var failed = await processor.ProcessPendingAsync(cancellationToken: Cancellation);

        failed.Should().Be(0);
        (await ReadAsync(database, "outbox_q")).Should().BeEmpty("the attempt was one transaction, so a later sink failing undid the enqueue");

        broken.Refuse = false;
        clock.Advance(TimeSpan.FromSeconds(5));
        var processed = await processor.ProcessPendingAsync(cancellationToken: Cancellation);

        processed.Should().Be(1);
        (await ReadAsync(database, "outbox_q")).Should().ContainSingle("and the retry enqueued it exactly once");

        await using var check = database.CreateContext();
        (await check.Outbox.SingleAsync(m => m.Id == id, Cancellation)).ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Queues_read_from_configuration_each_get_the_message()
    {
        var database = Database;
        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Pgmq:Sink:Queues:0"] = "config_a", ["Pgmq:Sink:Queues:1"] = "config_b" })
            .Build()
            .GetSection("Pgmq:Sink");
        var sink = new PgmqSink(NpgsqlDataSource.Create(database.ConnectionString), new PgmqSinkOptions().ReadFrom(section));

        await sink.SendAsync(Message(), Cancellation);

        (await ReadAsync(database, "config_a")).Should().ContainSingle();
        (await ReadAsync(database, "config_b")).Should().ContainSingle();
    }

    [Fact]
    public void The_queue_name_can_be_chosen_per_message()
    {
        var options = new PgmqSinkOptions().UseQueue(message => message.Name.Replace('.', '_').Replace('-', '_'));

        options.QueueName(Message()).Should().Be("library_shelf_opened");
    }

    [Fact]
    public void A_null_queue_selector_is_rejected_rather_than_silently_ignored()
    {
        var options = new PgmqSinkOptions();

        var nothing = () => options.UseQueue((Func<IntegrationEventMessage, string>)null!);
        var empty = () => options.UseQueue(" ");

        nothing.Should().Throw<ArgumentNullException>();
        empty.Should().Throw<ArgumentException>();
    }

    private async Task<IReadOnlyList<PgmqMessage>> ReadAsync(PgmqDatabase database, string queue)
    {
        await using var connection = await database.OpenAsync(Cancellation);
        return await PgmqQueue.ReadAsync(connection, null, queue, count: 10, cancellationToken: Cancellation);
    }

    /// <summary>Writes one pending outbox row and returns its id.</summary>
    private async Task<Guid> WriteRowAsync(PgmqContext context)
    {
        var domainEvent = new Domain.Events.ShelfCreated(Domain.ShelfId.CreateUnique(), "Fiction");

        context.Outbox.Add(new Outbox.OutboxMessage
        {
            Id = domainEvent.EventId,
            EventName = "shelf.created",
            Payload = JsonSerializer.Serialize(domainEvent, new Options.OutboxOptions().JsonOptions),
            OccurredAt = domainEvent.OccurredAt,
            CreatedAt = domainEvent.OccurredAt,
        });

        await context.SaveChangesAsync(Cancellation);
        return domainEvent.EventId;
    }

    /// <summary>The processor only uses the provider to resolve sinks registered by type; these are instances.</summary>
    private sealed class ServiceProviderStub : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
