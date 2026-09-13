using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Inbox;
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
/// The receiving half: running a handler once for a message and consumer, with the effect and the
/// "already applied" marker in one transaction.
/// </summary>
public sealed class InboxTests : IDisposable
{
    private const string Consumer = "billing.person-projector";

    private readonly SqliteDatabase _db = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));

    public void Dispose() => _db.Dispose();

    private TestHost CreateHost(Action<DDDEntityFrameworkOptions>? configure = null, Action<IServiceCollection>? services = null)
        => new(
            _db,
            options =>
            {
                options.TimeProvider = _clock;
                configure?.Invoke(options);
            },
            dispatchThroughRecorder: true,
            collection =>
            {
                collection.AddDomainEventInbox<LibraryContext>();
                services?.Invoke(collection);
            });

    private static Person NewPerson(string first) => new(MemberId.CreateUnique(), new PersonName(first, "Lovelace"), null, new ValidDateOfBirth(new DateOnly(1815, 12, 10)));

    /// <summary>Runs the inbox in a fresh scope, the way a consumer of one delivery would.</summary>
    private Task<bool> DeliverAsync(TestHost host, Guid messageId, Func<LibraryContext, CancellationToken, Task> handler, string consumer = Consumer)
        => host.InScopeAsync((context, services) => services
            .GetRequiredService<DomainEventInbox<LibraryContext>>()
            .ExecuteOnceAsync(messageId, consumer, token => handler(context, token)));

    private static Task ProjectAsync(LibraryContext context, CancellationToken cancellationToken)
    {
        context.People.Add(NewPerson("Ada"));
        return Task.CompletedTask;
    }

    private List<Person> People()
    {
        using var check = _db.CreateLibraryContext();
        return [.. check.People];
    }

    private List<InboxMessage> Rows()
    {
        using var check = _db.CreateLibraryContext();
        return [.. check.Inbox];
    }

    [Fact]
    public async Task The_handler_runs_once_however_often_the_message_is_delivered()
    {
        using var host = CreateHost();
        var messageId = Guid.CreateVersion7();

        var first = await DeliverAsync(host, messageId, ProjectAsync);
        var second = await DeliverAsync(host, messageId, ProjectAsync);
        var third = await DeliverAsync(host, messageId, ProjectAsync);

        first.Should().BeTrue();
        second.Should().BeFalse("the message had already been applied by this consumer");
        third.Should().BeFalse();

        People().Should().ContainSingle().Which.Name.FirstName.Should().Be("Ada");
        var row = Rows().Should().ContainSingle().Subject;
        row.MessageId.Should().Be(messageId);
        row.Consumer.Should().Be(Consumer);
        row.ProcessedAt.Should().Be(_clock.GetUtcNow());
        row.MessageName.Should().BeNull();
    }

    [Fact]
    public async Task A_crash_between_handling_and_marking_applies_nothing_and_the_redelivery_applies_it_once()
    {
        using var host = CreateHost();
        var messageId = Guid.CreateVersion7();

        var crash = async () => await DeliverAsync(host, messageId, async (context, token) =>
        {
            context.People.Add(NewPerson("Ada"));

            // The handler even saves for itself, so a naive inbox would already have written the row.
            await context.SaveChangesAsync(token);
            throw new InvalidOperationException("the process died here");
        });

        await crash.Should().ThrowAsync<InvalidOperationException>().WithMessage("the process died here");

        People().Should().BeEmpty("the effect is rolled back with the marker it never got");
        Rows().Should().BeEmpty();

        var applied = await DeliverAsync(host, messageId, ProjectAsync);

        applied.Should().BeTrue("nothing was recorded, so the redelivery is the first real attempt");
        People().Should().ContainSingle();
        Rows().Should().ContainSingle();
    }

    [Fact]
    public async Task Two_consumers_of_the_same_message_each_run_once()
    {
        using var host = CreateHost();
        var messageId = Guid.CreateVersion7();

        await DeliverAsync(host, messageId, ProjectAsync, "billing");
        await DeliverAsync(host, messageId, ProjectAsync, "billing");
        var other = await DeliverAsync(host, messageId, ProjectAsync, "search");

        other.Should().BeTrue("the key is the pair, so a consumer added later is not skipped");
        People().Should().HaveCount(2);
        Rows().Select(r => r.Consumer).Should().BeEquivalentTo("billing", "search");
    }

    [Fact]
    public async Task It_joins_the_caller_transaction_and_leaves_committing_to_them()
    {
        using var host = CreateHost();
        var messageId = Guid.CreateVersion7();

        await host.InScopeAsync(async (context, services) =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var inbox = services.GetRequiredService<DomainEventInbox<LibraryContext>>();

            var applied = await inbox.ExecuteOnceAsync(messageId, Consumer, token => ProjectAsync(context, token));

            applied.Should().BeTrue();
            await transaction.RollbackAsync();
            return applied;
        });

        People().Should().BeEmpty("the inbox joined the caller's transaction rather than committing its own");
        Rows().Should().BeEmpty();
    }

    [Fact]
    public async Task The_envelope_overload_keeps_the_published_name_on_the_row()
    {
        using var host = CreateHost();
        var message = new IntegrationEventMessage
        {
            MessageId = Guid.CreateVersion7(),
            Name = "library.shelf-opened",
            Version = 3,
            Payload = """{"ShelfId":"SHELF_1","DisplayName":"Fiction"}""",
            OccurredAt = _clock.GetUtcNow(),
        };

        var applied = await host.InScopeAsync((context, services) => services
            .GetRequiredService<DomainEventInbox<LibraryContext>>()
            .ExecuteOnceAsync(message, Consumer, (received, token) =>
            {
                received.Version.Should().Be(3);
                return ProjectAsync(context, token);
            }));

        applied.Should().BeTrue();
        Rows().Should().ContainSingle().Which.MessageName.Should().Be("library.shelf-opened");
    }

    [Fact]
    public async Task HasProcessedAsync_answers_per_consumer()
    {
        using var host = CreateHost();
        var messageId = Guid.CreateVersion7();
        await DeliverAsync(host, messageId, ProjectAsync);

        var answers = await host.InScopeAsync(async (_, services) =>
        {
            var inbox = services.GetRequiredService<DomainEventInbox<LibraryContext>>();
            return (
                Mine: await inbox.HasProcessedAsync(messageId, Consumer),
                Theirs: await inbox.HasProcessedAsync(messageId, "search"),
                Other: await inbox.HasProcessedAsync(Guid.CreateVersion7(), Consumer));
        });

        answers.Mine.Should().BeTrue();
        answers.Theirs.Should().BeFalse();
        answers.Other.Should().BeFalse();
    }

    [Fact]
    public async Task A_context_without_the_inbox_table_fails_with_guidance()
    {
        using var context = new NoOutboxContext(_db.Options<NoOutboxContext>());
        var inbox = new DomainEventInbox<NoOutboxContext>(context);

        var act = () => inbox.ExecuteOnceAsync(Guid.CreateVersion7(), Consumer, _ => Task.CompletedTask);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*NoOutboxContext*AddDomainEventInbox*");
    }

    [Fact]
    public async Task A_consumer_name_is_required()
    {
        using var host = CreateHost();

        var act = () => DeliverAsync(host, Guid.CreateVersion7(), ProjectAsync, consumer: "  ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task An_at_least_once_outbox_and_an_inbox_together_apply_the_effect_exactly_once()
    {
        // The full loop: the outbox redelivers because a second sink is broken, and the inbox behind
        // the first sink makes that redelivery harmless.
        var broken = new RecordingSink { Refuse = true };
        using var host = new TestHost(
            _db,
            options =>
            {
                options.TimeProvider = _clock;
                options.UseOutbox(outbox => outbox
                    .RegisterEvent<ShelfCreated>()
                    .SendTo<ProjectingSink>()
                    .SendTo(broken));
            },
            dispatchThroughRecorder: false,
            services =>
            {
                services.AddOutboxProcessor<LibraryContext>();
                services.AddDomainEventInbox<LibraryContext>();
            });

        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(new Shelf(ShelfId.CreateUnique(), "Fiction", UserId.CreateUnique(), CatId.CreateUnique(), null));
            await context.SaveChangesAsync();
        });

        var firstRun = await host.InScopeAsync((_, services) => services.GetRequiredService<OutboxProcessor<LibraryContext>>().ProcessPendingAsync());
        firstRun.Should().Be(0, "the broken sink fails the message as a whole");
        People().Should().ContainSingle("the working sink did apply it");

        broken.Refuse = false;
        var secondRun = await host.InScopeAsync((_, services) => services.GetRequiredService<OutboxProcessor<LibraryContext>>().ProcessPendingAsync());

        secondRun.Should().Be(1);
        People().Should().ContainSingle("the sink saw the message twice; the inbox applied it once");
        Rows().Should().ContainSingle().Which.Consumer.Should().Be("library.person-projector");
    }

    /// <summary>A sink that consumes through the inbox, which is how a real consumer would be written.</summary>
    private sealed class ProjectingSink(DomainEventInbox<LibraryContext> inbox, LibraryContext context) : IIntegrationEventSink
    {
        public Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default)
            => inbox.ExecuteOnceAsync(message, "library.person-projector", (_, token) => ProjectAsync(context, token), cancellationToken);
    }
}
