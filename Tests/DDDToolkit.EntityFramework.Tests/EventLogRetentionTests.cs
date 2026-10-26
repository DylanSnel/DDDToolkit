using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.EventLog;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Storage;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// How an event log lets go of its rows: by a window of its own, which the outbox's clean-up knows nothing
/// about, and never past what the log's own table keeps. On SQLite, and on Postgres with the guard on the table
/// and under a login role that holds nothing, where the bookkeeping role does the deleting. The system clock
/// throughout, because the guard reads the database's.
/// </summary>
public sealed class EventLogRetentionTests(ExplicitCallersPostgres postgres) : IDisposable
{
    private static readonly TimeSpan KeepFor = LoggedApiaryContext.KeepFor;

    private readonly SqliteDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>The apiary with its log and retention, on SQLite or on Postgres, and how the assertions reach its tables.</summary>
    private sealed record Apiary(ServiceProvider Host, Func<DbContext> Owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Host.DisposeAsync();

        public async Task SeedAsync(params object[] rows)
        {
            await using var owner = Owner();
            owner.AddRange(rows);
            await owner.SaveChangesAsync(Cancellation);
        }

        public async Task<List<string>> LogAsync()
        {
            await using var owner = Owner();
            return await owner.Set<EventLogEntry>().Select(entry => entry.EventName).OrderBy(name => name).ToListAsync(Cancellation);
        }

        public async Task<List<string>> OutboxAsync()
        {
            await using var owner = Owner();
            return await owner.Set<OutboxMessage>().Select(message => message.EventName).OrderBy(name => name).ToListAsync(Cancellation);
        }

        /// <summary>One run of retention, as the hosted service does it: in a scope of its own, as the toolkit's bookkeeping.</summary>
        public Task<DomainEventRetentionResult> RunAsync()
            => Host.GetServices<IHostedService>().OfType<DomainEventRetentionService<ApiaryContext>>().Single().DeleteExpiredAsync(Cancellation);
    }

    private async Task<Apiary> StartAsync(bool onPostgres, Action<DomainEventRetentionOptions<ApiaryContext>> retention)
    {
        if (onPostgres)
        {
            var database = await ApiaryDatabase.CreateAsync(postgres, logged: true);
            return new Apiary(database.BuildHost(services => services.AddDomainEventRetention(retention)), database.ModelContext);
        }

        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddDDDToolkitEntityFramework(options => options.UseOutbox<ApiaryContext>(outbox => outbox.RegisterEvent<HiveSettled>().KeepEventLog()));
        collection.AddDbContext<ApiaryContext, LoggedApiaryContext>((provider, options) => options.UseSqlite(_db.Connection).UseDDDToolkit(provider));
        collection.AddDomainEventRetention(retention);

        _db.EnsureCreated(() => new LoggedApiaryContext(_db.Options<LoggedApiaryContext>()));
        return new Apiary(collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }), () => new LoggedApiaryContext(_db.Options<LoggedApiaryContext>()));
    }

    private static EventLogEntry Recorded(string name, TimeSpan ago) => new()
    {
        Id = Guid.CreateVersion7(),
        EventName = name,
        Payload = "{}",
        OccurredAt = DateTimeOffset.UtcNow - ago,
        RecordedAt = DateTimeOffset.UtcNow - ago,
        ActedByKind = ActedByKinds.System,
    };

    private static OutboxMessage Delivered(string name, TimeSpan ago) => new()
    {
        Id = Guid.CreateVersion7(),
        EventName = name,
        Payload = "{}",
        OccurredAt = DateTimeOffset.UtcNow - ago,
        CreatedAt = DateTimeOffset.UtcNow - ago,
        ProcessedAt = DateTimeOffset.UtcNow - ago,
        Attempts = 1,
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retention_deletes_log_rows_older_than_keep_event_log_for_and_nothing_younger(bool onPostgres)
    {
        await using var apiary = await StartAsync(onPostgres, retention =>
        {
            retention.KeepEventLogFor = TimeSpan.FromDays(90);
            retention.BatchSize = 2;
        });
        await apiary.SeedAsync(
            Recorded("a year old", TimeSpan.FromDays(365)),
            Recorded("a hundred days old", TimeSpan.FromDays(100)),
            Recorded("ninety-one days old", TimeSpan.FromDays(91)),
            Recorded("eighty-nine days old", TimeSpan.FromDays(89)),
            Recorded("past the table's period, inside the window", KeepFor + TimeSpan.FromDays(1)),
            Recorded("today's", TimeSpan.FromHours(1)));

        var result = await apiary.RunAsync();

        result.Should().Be(new DomainEventRetentionResult(OutboxMessages: 0, InboxMessages: 0) { EventLogEntries = 3 }, "three rows are older than the window, and they go two at a time");
        (await apiary.LogAsync()).Should().Equal("eighty-nine days old", "past the table's period, inside the window", "today's");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Outbox_clean_up_leaves_the_log_alone(bool onPostgres)
    {
        await using var apiary = await StartAsync(onPostgres, retention => retention.KeepOutboxFor = TimeSpan.FromDays(7));
        await apiary.SeedAsync(
            Delivered("delivered last month", TimeSpan.FromDays(30)),
            Delivered("delivered yesterday", TimeSpan.FromDays(1)),
            Recorded("ten years old", TimeSpan.FromDays(3650)),
            Recorded("yesterday's", TimeSpan.FromDays(1)));

        var result = await apiary.RunAsync();

        result.Should().Be(new DomainEventRetentionResult(OutboxMessages: 1, InboxMessages: 0));
        (await apiary.OutboxAsync()).Should().Equal("delivered yesterday");
        (await apiary.LogAsync()).Should().Equal(["ten years old", "yesterday's"], "the outbox's window says nothing about the log, which has one of its own or none");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_window_equal_to_the_tables_keep_for_stays_a_minute_behind_it(bool onPostgres)
    {
        await using var apiary = await StartAsync(onPostgres, retention => retention.KeepEventLogFor = KeepFor);
        await apiary.SeedAsync(
            Recorded("well past", KeepFor + TimeSpan.FromMinutes(2)),
            Recorded("just past", KeepFor + TimeSpan.FromSeconds(20)),
            Recorded("just inside", KeepFor - TimeSpan.FromSeconds(20)));

        var result = await apiary.RunAsync();

        // The row twenty seconds past the line is one the guard would let go by its own clock, and one a clock a
        // few seconds off would send it too early. Retention waits the minute out, and no statement is refused.
        result.EventLogEntries.Should().Be(1);
        (await apiary.LogAsync()).Should().Equal("just inside", "just past");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_retention_shorter_than_the_guards_keep_for_is_refused(bool onPostgres)
    {
        await using var apiary = await StartAsync(onPostgres, retention =>
        {
            retention.KeepOutboxFor = TimeSpan.FromDays(7);
            retention.KeepEventLogFor = KeepFor - TimeSpan.FromDays(1);
        });
        await apiary.SeedAsync(Delivered("delivered last month", TimeSpan.FromDays(30)), Recorded("old", TimeSpan.FromDays(365)));

        var run = () => apiary.RunAsync();

        (await run.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "KeepEventLogFor is 29.00:00:00, but the event log of 'LoggedApiaryContext' keeps a row for 30.00:00:00, and its table refuses to delete a younger one. Set KeepEventLogFor to 30.00:00:00 or longer, or give AddEventLog a shorter keepFor.");
        (await apiary.OutboxAsync()).Should().Equal(["delivered last month"], "a window the log would refuse is a mistake in the settings, not half a run");
        (await apiary.LogAsync()).Should().Equal("old");
    }

    [Fact]
    public async Task Retention_deletes_log_rows_as_the_system_role()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres, logged: true);
        await using var host = database.BuildHost(services => services.AddDomainEventRetention<ApiaryContext>(retention => retention.KeepEventLogFor = TimeSpan.FromDays(60)));
        await using (var seed = database.ModelContext())
        {
            seed.AddRange(Recorded("old", TimeSpan.FromDays(61)), Recorded("recent", TimeSpan.FromDays(59)));
            await seed.SaveChangesAsync(Cancellation);
        }

        // Who deleted, noted by the database: the trigger runs as whoever deletes.
        await database.RunAsOwnerAsync(
            """
            CREATE TABLE ddd.deleted_by (who text NOT NULL);
            GRANT INSERT ON ddd.deleted_by TO PUBLIC;
            CREATE FUNCTION ddd.note_who_deleted() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN INSERT INTO ddd.deleted_by VALUES (current_user); RETURN NULL; END $$;
            CREATE TRIGGER note_who_deleted AFTER DELETE ON ddd."EventLog" FOR EACH ROW EXECUTE FUNCTION ddd.note_who_deleted();
            """,
            Cancellation);

        // Called by hand with nobody begun, retention is the host's work, and the host said nothing.
        await using (var scope = host.CreateAsyncScope())
        {
            var byHand = () => scope.ServiceProvider.GetRequiredService<DomainEventRetention<ApiaryContext>>().DeleteExpiredAsync(Cancellation);
            await byHand.Should().ThrowAsync<NoCallerException>();
        }

        // No caller a request runs as may delete a row of the log: the privilege is the bookkeeping role's alone.
        await ApiaryDatabase.AsAsync(host, ApiaryDatabase.Alice, async context =>
        {
            var delete = () => context.Set<EventLogEntry>().ExecuteDeleteAsync(Cancellation);
            (await delete.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be(Npgsql.PostgresErrorCodes.InsufficientPrivilege);
        });

        var service = host.GetServices<IHostedService>().OfType<DomainEventRetentionService<ApiaryContext>>().Single();
        var result = await service.DeleteExpiredAsync(Cancellation);

        result.EventLogEntries.Should().Be(1);
        (await database.ListAsOwnerAsync("""SELECT "EventName" FROM ddd."EventLog" """, Cancellation)).Should().Equal("recent");
        (await database.ListAsOwnerAsync("SELECT who FROM ddd.deleted_by", Cancellation)).Should().Equal(["ddd_system"], "the service begins the system caller, which under a login role that holds nothing is the bookkeeping role");
    }

    [Fact]
    public async Task A_log_that_keeps_every_row_refuses_retention()
    {
        await using var context = new AnnalsContext(_db.Options<AnnalsContext>());
        await context.Database.EnsureCreatedAsync(Cancellation);
        context.Annals.Add(Recorded("ancient", TimeSpan.FromDays(3650)));
        await context.SaveChangesAsync(Cancellation);
        var retention = new DomainEventRetention<AnnalsContext>(context, new DomainEventRetentionOptions<AnnalsContext> { KeepEventLogFor = TimeSpan.FromDays(365) });

        var run = () => retention.DeleteExpiredAsync(Cancellation);
        var byCutoff = () => retention.DeleteEventLogEntriesAsync(DateTimeOffset.UtcNow.AddYears(-50), Cancellation);

        (await run.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "The event log of 'AnnalsContext' keeps every row: AddEventLog was given no keepFor, so nothing may delete one. Give it a keepFor to let old rows go, or leave KeepEventLogFor unset.");
        await byCutoff.Should().ThrowAsync<InvalidOperationException>();
        (await context.Annals.CountAsync(Cancellation)).Should().Be(1);
    }

    [Fact]
    public async Task A_cutoff_that_reaches_rows_the_log_still_keeps_is_refused()
    {
        await using var apiary = await StartAsync(onPostgres: false, retention => retention.KeepEventLogFor = TimeSpan.FromDays(365));
        await apiary.SeedAsync(Recorded("old", TimeSpan.FromDays(200)), Recorded("recent", TimeSpan.FromDays(40)));
        await using var scope = apiary.Host.CreateAsyncScope();
        var retention = scope.ServiceProvider.GetRequiredService<DomainEventRetention<ApiaryContext>>();

        var tooRecent = () => retention.DeleteEventLogEntriesAsync(DateTimeOffset.UtcNow - KeepFor + TimeSpan.FromHours(1), Cancellation);
        (await tooRecent.Should().ThrowAsync<ArgumentOutOfRangeException>()).WithMessage("The event log of 'LoggedApiaryContext' keeps a row for 30.00:00:00, so only rows recorded before *");

        (await retention.DeleteEventLogEntriesAsync(DateTimeOffset.UtcNow.AddDays(-100), Cancellation)).Should().Be(1);
        (await apiary.LogAsync()).Should().Equal("recent");
    }

    [Fact]
    public async Task A_window_on_a_log_the_context_does_not_map_fails_with_guidance()
    {
        await using var context = new ApiaryContext(_db.Options<ApiaryContext>());
        var retention = new DomainEventRetention<ApiaryContext>(context, new DomainEventRetentionOptions<ApiaryContext> { KeepEventLogFor = TimeSpan.FromDays(1) });

        var run = () => retention.DeleteExpiredAsync(Cancellation);

        (await run.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "The model of 'ApiaryContext' does not contain the event log table. Call modelBuilder.AddEventLog() in OnModelCreating, or leave KeepEventLogFor unset.");
    }

    [Fact]
    public void The_logs_window_alone_is_a_registration_and_no_window_at_all_is_not()
    {
        var services = new ServiceCollection().AddDomainEventRetention<ApiaryContext>(retention => retention.KeepEventLogFor = TimeSpan.FromDays(365));

        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(DomainEventRetentionOptions<ApiaryContext>))
            .Which.ImplementationInstance.Should().BeOfType<DomainEventRetentionOptions<ApiaryContext>>()
            .Which.KeepEventLogFor.Should().Be(TimeSpan.FromDays(365));

        var none = () => new ServiceCollection().AddDomainEventRetention<ApiaryContext>(_ => { });
        none.Should().Throw<ArgumentException>().WithMessage("Set KeepOutboxFor, KeepInboxFor or KeepEventLogFor. With none, retention has nothing to delete.*");

        var negative = () => new ServiceCollection().AddDomainEventRetention<ApiaryContext>(retention => retention.KeepEventLogFor = TimeSpan.Zero);
        negative.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be(nameof(DomainEventRetentionOptions<ApiaryContext>.KeepEventLogFor));
    }
}
