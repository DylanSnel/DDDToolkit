using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.EventLog;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Storage;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The event log: a context that keeps its events writes one row per kept event in the save that raised it, next
/// to the event's outbox row, with who acted and the columns the module added. On SQLite, where nothing but the
/// toolkit itself keeps the table from changing; <see cref="EventLogGuardTests"/> holds the guard on Postgres.
/// </summary>
public sealed class EventLogTests : IDisposable
{
    private readonly SqliteDatabase _db = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

    public void Dispose() => _db.Dispose();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// The apiary's host on SQLite: an outbox for the hives' events that keeps what <paramref name="keep"/> chooses,
    /// every event when left out, on a context whose model maps the log, unless <paramref name="mapped"/> says not.
    /// </summary>
    private ServiceProvider Host(Action<EventLogOptions>? keep = null, Action<IServiceCollection>? services = null, bool mapped = true)
    {
        var collection = new ServiceCollection();
        collection.AddDDDToolkitEntityFramework(options =>
        {
            options.TimeProvider = _clock;
            options.UseOutbox<ApiaryContext>(outbox => outbox.RegisterEvent<HiveSettled>().RegisterEvent<HiveRenamed>().KeepEventLog(keep));
        });

        void Configure(IServiceProvider provider, DbContextOptionsBuilder options) => options.UseSqlite(_db.Connection).UseDDDToolkit(provider);
        if (mapped)
        {
            collection.AddDbContext<ApiaryContext, LoggedApiaryContext>(Configure);
        }
        else
        {
            collection.AddDbContext<ApiaryContext>(Configure);
        }

        services?.Invoke(collection);
        var provider = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApiaryContext>().Database.EnsureCreated();
        return provider;
    }

    private static async Task<T> InScopeAsync<T>(IServiceProvider host, Func<ApiaryContext, Task<T>> work)
    {
        await using var scope = host.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<ApiaryContext>());
    }

    private static Task InScopeAsync(IServiceProvider host, Func<ApiaryContext, Task> work)
        => InScopeAsync<object?>(host, async context =>
        {
            await work(context);
            return null;
        });

    private static Task<List<EventLogEntry>> LogAsync(IServiceProvider host)
        => InScopeAsync(host, context => context.Set<EventLogEntry>().AsNoTracking().OrderBy(entry => entry.EventName).ToListAsync(Cancellation));

    private static Hive NewHive(int number = 1, Guid? keeper = null) => new(HiveId.CreateSequential(), number, "By the hedge", keeper, isOpen: true);

    [Fact]
    public async Task A_kept_event_is_written_in_the_same_save()
    {
        await using var host = Host();
        var hive = NewHive();
        var settled = ((IHasDomainEvents)hive).DomainEvents.Should().ContainSingle().Which;

        await InScopeAsync(host, async context =>
        {
            context.Hives.Add(hive);
            await context.SaveChangesAsync(Cancellation);
        });

        var message = await InScopeAsync(host, context => context.Outbox.AsNoTracking().SingleAsync(Cancellation));
        var entry = (await LogAsync(host)).Should().ContainSingle().Which;

        entry.Id.Should().Be(settled.EventId).And.Be(message.Id, "the log row, the outbox row and the event share one id");
        entry.EventName.Should().Be("hive.settled");
        entry.Version.Should().Be(1);
        entry.Payload.Should().Be(message.Payload, "the same payload as the outbox row, written with the outbox's serializer");
        entry.OccurredAt.Should().Be(settled.OccurredAt);
        entry.RecordedAt.Should().Be(_clock.GetUtcNow(), "the application's clock says when the row was written");
        entry.AggregateType.Should().Be(nameof(Hive));
        entry.AggregateId.Should().Be(hive.Id.ToString());
        (entry.ActedByKind, entry.ActedById).Should().Be((ActedByKinds.System, null), "nothing began a caller, so the application itself acted");

        // A second save adds its events to the log and leaves the first row alone.
        _clock.Advance(TimeSpan.FromMinutes(5));
        await InScopeAsync(host, async context =>
        {
            (await context.Hives.SingleAsync(Cancellation)).Rename("By the gate");
            await context.SaveChangesAsync(Cancellation);
        });

        var log = await LogAsync(host);
        log.Select(row => row.EventName).Should().Equal("hive.renamed", "hive.settled");
        log[1].Should().BeEquivalentTo(entry, "a row of the log is never written twice");
        log[0].RecordedAt.Should().Be(entry.RecordedAt + TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task A_failed_save_keeps_no_event()
    {
        await using var host = Host();
        var first = NewHive();
        var settled = ((IHasDomainEvents)first).DomainEvents.Single();
        await InScopeAsync(host, async context =>
        {
            context.Hives.Add(first);
            await context.SaveChangesAsync(Cancellation);
        });

        // A second hive under the first one's key: the database refuses the save, and with it everything in it.
        await InScopeAsync(host, async context =>
        {
            context.Hives.Add(new Hive(first.Id, number: 2, "A double", keeper: null, isOpen: true));
            var save = () => context.SaveChangesAsync(Cancellation);
            await save.Should().ThrowAsync<DbUpdateException>();
        });

        (await LogAsync(host)).Should().ContainSingle("the log row of a save that failed went with it").Which.OccurredAt.Should().Be(((HiveSettled)settled).OccurredAt);
        _db.CountRows("OutboxMessages").Should().Be(1);
        _db.CountRows("Hives").Should().Be(1);
    }

    [Fact]
    public async Task Only_the_chosen_events_are_kept()
    {
        await using var host = Host(log => log.Keep<HiveRenamed>().Only(domainEvent => domainEvent is HiveSettled { Number: > 10 }));

        var small = NewHive(number: 3);
        var big = NewHive(number: 12);
        await InScopeAsync(host, async context =>
        {
            small.Rename("Small, renamed");
            context.Hives.AddRange(small, big);
            await context.SaveChangesAsync(Cancellation);
        });

        (await LogAsync(host)).Select(entry => entry.EventName + " of the " + (entry.AggregateId == big.Id.ToString() ? "big" : "small") + " hive").Should().Equal(
            ["hive.renamed of the small hive", "hive.settled of the big hive"],
            "the renaming is kept by its type and the big hive's settling by the predicate; the small hive's settling is neither");
        _db.CountRows("OutboxMessages").Should().Be(3, "what the log keeps says nothing about what the outbox delivers");
    }

    [Fact]
    public async Task Who_acted_comes_from_the_accessor()
    {
        await using var host = Host();
        var alice = Guid.Parse("a11ce000-0000-4000-8000-000000000031");

        async Task<(string Kind, string? Id)> SettledAsAsync(IServiceProvider services, Caller? caller, int number)
        {
            using var running = caller is null ? null : Callers.Begin(caller);
            var id = HiveId.CreateSequential();
            await InScopeAsync(services, async context =>
            {
                context.Hives.Add(new Hive(id, number, "By the hedge", keeper: null, isOpen: true));
                await context.SaveChangesAsync(Cancellation);
            });

            var entry = await InScopeAsync(services, context => context.Set<EventLogEntry>().AsNoTracking().SingleAsync(row => row.AggregateId == id.ToString(), Cancellation));
            return (entry.ActedByKind, entry.ActedById);
        }

        // The default accessor reads the toolkit's own caller, each time it is asked.
        (await SettledAsAsync(host, Callers.FromClaims($$"""{"sub":"{{alice}}","role":"authenticated"}"""), 1)).Should().Be((ActedByKinds.User, alice.ToString()));
        (await SettledAsAsync(host, Callers.FromClaims("""{"sub":"auth0|ada","role":"authenticated"}"""), 2)).Should().Be((ActedByKinds.User, "auth0|ada"), "a user whose id is no uuid is kept by the token's sub");
        (await SettledAsAsync(host, Caller.SystemIn("apiary"), 3)).Should().Be((ActedByKinds.System, "apiary"), "scoped system work is the system, in its scope");
        (await SettledAsAsync(host, Caller.System, 4)).Should().Be((ActedByKinds.System, null));
        (await SettledAsAsync(host, Caller.Anonymous, 5)).Should().Be((ActedByKinds.Anonymous, null));
        (await SettledAsAsync(host, caller: null, 6)).Should().Be((ActedByKinds.System, null), "outside any caller the application itself acts, as in every host that requires none");

        // A host whose caller accessor knows the request is asked through it.
        await using (var requests = Host(services: services => services.AddSingleton<ICallerAccessor>(new CallerOfTheTest { Current = Callers.FromClaims($$"""{"sub":"{{alice}}"}""") })))
        {
            (await SettledAsAsync(requests, caller: null, 7)).Should().Be((ActedByKinds.User, alice.ToString()));
        }

        // A package or a host that knows more kinds of actor registers an accessor of its own, which then answers.
        await using var named = Host(services: services => services.AddSingleton<IActedByAccessor>(new Named("seat", "front-row-4")));
        (await SettledAsAsync(named, Callers.FromClaims($$"""{"sub":"{{alice}}"}"""), 8)).Should().Be(("seat", "front-row-4"));
    }

    [Fact]
    public async Task The_fillers_fill_the_modules_own_columns()
    {
        var yards = new YardOfTheFlow();
        var stamps = new Stamped();
        await using var host = Host(services: services => services.AddSingleton<IEventLogFields>(yards).AddSingleton<IEventLogFields>(stamps));

        using (YardOfTheFlow.Begin("north"))
        {
            await InScopeAsync(host, async context =>
            {
                var hive = NewHive();
                hive.Rename("Renamed at once");
                context.Hives.Add(hive);
                await context.SaveChangesAsync(Cancellation);
            });
        }

        using (YardOfTheFlow.Begin("south"))
        {
            await InScopeAsync(host, async context =>
            {
                context.Hives.Add(NewHive(number: 2));
                await context.SaveChangesAsync(Cancellation);
            });
        }

        var log = await InScopeAsync(host, context => context.Set<EventLogEntry>().AsNoTracking()
            .OrderBy(entry => entry.RecordedAt).ThenBy(entry => entry.EventName)
            .Select(entry => entry.EventName + " in " + EF.Property<string?>(entry, LoggedApiaryContext.Yard))
            .ToListAsync(Cancellation));

        log.Should().BeEquivalentTo(["hive.renamed in north", "hive.settled in north", "hive.settled in south"], "a filler reads what it fills in each time it is asked");
        yards.Asked.Should().Be(3, "every filler is asked once for every kept event");
        stamps.Seen.Should().Equal(["HiveSettled:hive.settled", "HiveRenamed:hive.renamed", "HiveSettled:hive.settled"], "in the order the events were raised, with the row as it is about to be saved");
    }

    [Fact]
    public async Task An_outbox_that_keeps_a_log_the_model_does_not_map_fails_and_keeps_its_events()
    {
        await using var host = Host(mapped: false);
        var hive = NewHive();

        await InScopeAsync(host, async context =>
        {
            context.Hives.Add(hive);
            var save = () => context.SaveChangesAsync(Cancellation);

            (await save.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
                "The outbox of 'ApiaryContext' keeps an event log but the context's model does not contain the event log table. Call modelBuilder.AddEventLog(Database) in OnModelCreating, or take KeepEventLog() out. Nothing was saved.");
        });

        ((IHasDomainEvents)hive).DomainEvents.Should().ContainSingle("the event never left its aggregate, so a save that is put right still delivers it");
        _db.CountRows("Hives").Should().Be(0);
    }

    [Fact]
    public void The_log_is_mapped_next_to_the_outbox_and_marked_as_a_table_that_only_grows()
    {
        using var context = new LoggedApiaryContext(_db.Options<LoggedApiaryContext>());
        var log = context.Model.FindEntityType(typeof(EventLogEntry))!;

        log.GetTableName().Should().Be(EventLogModelBuilderExtensions.DefaultTableName).And.Be("EventLog");
        log.GetSchema().Should().Be(DomainEventStorage.DefaultSchema, "the log lives next to the outbox, away from the module's own tables");
        log.FindPrimaryKey()!.Properties.Select(property => property.Name).Should().Equal(nameof(EventLogEntry.Id));
        log.GetIndexes().Should().ContainSingle().Which.Properties.Select(property => property.Name).Should().Equal(nameof(EventLogEntry.RecordedAt));
        log.FindProperty(LoggedApiaryContext.Yard).Should().NotBeNull("the module's own column is part of the table");

        log.FindAnnotation("DDDToolkit:AppendOnly")!.Value.Should().Be(true);
        log.FindAnnotation("DDDToolkit:AppendOnly:RecordedAt")!.Value.Should().Be(nameof(EventLogEntry.RecordedAt));
        log.FindAnnotation("DDDToolkit:AppendOnly:KeepForSeconds")!.Value.Should().Be(30L * 24 * 60 * 60);

        // A log kept for good carries no period, under a name and in a schema of its own.
        using var forever = new AnnalsContext(_db.Options<AnnalsContext>());
        var annals = forever.Model.FindEntityType(typeof(EventLogEntry))!;
        (annals.GetSchema(), annals.GetTableName()).Should().Be(("archive", "Annals"));
        annals.FindAnnotation("DDDToolkit:AppendOnly:KeepForSeconds").Should().BeNull();
    }

    [Fact]
    public void How_long_a_log_keeps_its_rows_is_the_applications_to_know_and_needs_no_migration()
    {
        using var month = new LoggedApiaryContext(new DbContextOptionsBuilder<LoggedApiaryContext>().UseNpgsql("Host=nowhere.invalid").Options);
        using var year = new YearLoggedApiaryContext(new DbContextOptionsBuilder<YearLoggedApiaryContext>().UseNpgsql("Host=nowhere.invalid").Options);

        var before = month.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var after = year.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var differ = month.GetService<IMigrationsModelDiffer>();

        differ.GetDifferences(before, after).Should().BeEmpty("the period is in the guard, which the next access script writes, and not in the table");
        year.Database.GenerateCreateScript().Should().Be(month.Database.GenerateCreateScript());

        // The script is where the period goes: thirty days, or a year, in seconds.
        PostgresRowAccess.Script(month, []).Should().Contain("EXECUTE FUNCTION ddd.refuse_changes_to_kept_rows('2592000', 'RecordedAt');");
        PostgresRowAccess.Script(year, []).Should().Contain("EXECUTE FUNCTION ddd.refuse_changes_to_kept_rows('31536000', 'RecordedAt');");
    }

    [Fact]
    public void A_keep_for_that_is_not_positive_or_longer_than_any_calendar_is_refused()
    {
        using var context = new ApiaryContext(_db.Options<ApiaryContext>());

        foreach (var keepFor in (TimeSpan[])[TimeSpan.Zero, TimeSpan.FromDays(-1), TimeSpan.MaxValue])
        {
            var map = () => new ModelBuilder().AddEventLog(context.Database, keepFor: keepFor);
            map.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*at most 365250 days*leave it out*").Which.ParamName.Should().Be("keepFor");
        }

        var rounded = new ModelBuilder().AddEventLog(context.Database, keepFor: TimeSpan.FromMilliseconds(1500)).Model.FindEntityType(typeof(EventLogEntry))!;
        rounded.FindAnnotation("DDDToolkit:AppendOnly:KeepForSeconds")!.Value.Should().Be(2L, "a guard that kept a row for less than was asked would be off the wrong way");

        var unnamed = () => new ModelBuilder().AddEventLog(context.Database, tableName: " ");
        unnamed.Should().Throw<ArgumentException>();
    }

    /// <summary>An accessor of a package that knows another kind of actor than the toolkit's own.</summary>
    private sealed class Named(string kind, string id) : IActedByAccessor
    {
        public ActedBy Current => new(kind, id);
    }

    /// <summary>A filler that notes each event it is asked about and the row it is given.</summary>
    private sealed class Stamped : IEventLogFields
    {
        public List<string> Seen { get; } = [];

        public void Fill(IDomainEvent domainEvent, EntityEntry<EventLogEntry> entry)
        {
            entry.State.Should().Be(EntityState.Added);
            Seen.Add(domainEvent.GetType().Name + ":" + entry.Entity.EventName);
        }
    }
}
