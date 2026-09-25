using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Storage;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Keeping the outbox and the inbox from growing forever: a row goes once it is older than the window
/// for its table, and nothing else does.
/// </summary>
public sealed class RetentionTests : IDisposable
{
    private readonly SqliteDatabase _db = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));

    public void Dispose() => _db.Dispose();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private TestHost CreateHost(Action<DomainEventRetentionOptions<LibraryContext>> retention)
        => new(
            _db,
            options => options.TimeProvider = _clock,
            dispatchThroughRecorder: true,
            services => services
                .AddDomainEventInbox<LibraryContext>()
                .AddDomainEventRetention(retention));

    private static Task<DomainEventRetentionResult> DeleteExpiredAsync(TestHost host)
        => host.InScopeAsync((_, services) => services.GetRequiredService<DomainEventRetention<LibraryContext>>().DeleteExpiredAsync(Cancellation));

    private void Seed(params object[] rows)
    {
        using var context = _db.CreateLibraryContext();
        context.AddRange(rows);
        context.SaveChanges();
    }

    private OutboxMessage Delivered(string name, TimeSpan ago) => Written(name, ago, processed: true, attempts: 1);

    private OutboxMessage Waiting(string name, TimeSpan ago, int attempts = 0) => Written(name, ago, processed: false, attempts);

    private OutboxMessage Written(string name, TimeSpan ago, bool processed, int attempts) => new()
    {
        Id = Guid.CreateVersion7(),
        EventName = name,
        Payload = "{}",
        OccurredAt = _clock.GetUtcNow() - ago,
        CreatedAt = _clock.GetUtcNow() - ago,
        ProcessedAt = processed ? _clock.GetUtcNow() - ago : null,
        Attempts = attempts,
    };

    private InboxMessage Applied(string consumer, TimeSpan ago) => new()
    {
        MessageId = Guid.CreateVersion7(),
        Consumer = consumer,
        ProcessedAt = _clock.GetUtcNow() - ago,
    };

    private List<string> OutboxNames()
    {
        using var check = _db.CreateLibraryContext();
        return [.. check.Outbox.Select(m => m.EventName)];
    }

    private List<string> InboxConsumers()
    {
        using var check = _db.CreateLibraryContext();
        return [.. check.Inbox.Select(m => m.Consumer)];
    }

    [Fact]
    public async Task Delivered_outbox_rows_older_than_the_window_are_deleted_and_nothing_else_is()
    {
        using var host = CreateHost(retention => retention.KeepOutboxFor = TimeSpan.FromDays(7));
        Seed(
            Delivered("old", TimeSpan.FromDays(8)),
            Delivered("recent", TimeSpan.FromDays(6)),
            Waiting("waiting", TimeSpan.FromDays(30)),
            Waiting("given-up", TimeSpan.FromDays(30), attempts: 10));

        var result = await DeleteExpiredAsync(host);

        result.Should().Be(new DomainEventRetentionResult(OutboxMessages: 1, InboxMessages: 0));
        OutboxNames().Should().BeEquivalentTo(["recent", "waiting", "given-up"], "a row still waiting, or given up on, is not history yet");
    }

    [Fact]
    public async Task Inbox_rows_older_than_the_window_are_deleted()
    {
        using var host = CreateHost(retention => retention.KeepInboxFor = TimeSpan.FromDays(30));
        Seed(Applied("old", TimeSpan.FromDays(31)), Applied("recent", TimeSpan.FromDays(29)));

        var result = await DeleteExpiredAsync(host);

        result.Should().Be(new DomainEventRetentionResult(OutboxMessages: 0, InboxMessages: 1));
        InboxConsumers().Should().Equal("recent");
    }

    [Fact]
    public async Task A_table_without_a_window_is_left_alone()
    {
        using var host = CreateHost(retention => retention.KeepInboxFor = TimeSpan.FromDays(1));
        Seed(Delivered("ancient", TimeSpan.FromDays(3650)), Applied("old", TimeSpan.FromDays(2)));

        var result = await DeleteExpiredAsync(host);

        result.Should().Be(new DomainEventRetentionResult(OutboxMessages: 0, InboxMessages: 1));
        OutboxNames().Should().Equal("ancient");
    }

    [Fact]
    public async Task A_backlog_larger_than_one_batch_is_deleted_batch_by_batch_until_none_is_left()
    {
        using var host = CreateHost(retention =>
        {
            retention.KeepInboxFor = TimeSpan.FromDays(1);
            retention.BatchSize = 2;
        });
        Seed([.. Enumerable.Range(0, 5).Select(i => Applied($"old-{i}", TimeSpan.FromDays(2))), Applied("recent", TimeSpan.FromHours(1))]);

        var result = await DeleteExpiredAsync(host);

        result.InboxMessages.Should().Be(5);
        InboxConsumers().Should().Equal("recent");
    }

    [Fact]
    public async Task A_message_whose_inbox_row_was_deleted_is_applied_again_when_it_comes_back()
    {
        // The price of retention, shown rather than described: the row is what makes a repeat a repeat.
        using var host = CreateHost(retention => retention.KeepInboxFor = TimeSpan.FromDays(30));
        var messageId = Guid.CreateVersion7();
        var runs = 0;

        Task<bool> DeliverAsync() => host.InScopeAsync((_, services) => services
            .GetRequiredService<DomainEventInbox<LibraryContext>>()
            .ExecuteOnceAsync(messageId, "billing", _ =>
            {
                runs++;
                return Task.CompletedTask;
            }, Cancellation));

        (await DeliverAsync()).Should().BeTrue();
        (await DeliverAsync()).Should().BeFalse();

        _clock.Advance(TimeSpan.FromDays(31));
        await DeleteExpiredAsync(host);

        (await DeliverAsync()).Should().BeTrue("nothing remembers it any more, which is why the window has to outlast every redelivery");
        runs.Should().Be(2);
    }

    [Fact]
    public async Task The_background_service_runs_the_retention_in_a_scope_of_its_own()
    {
        using var host = CreateHost(retention => retention.KeepInboxFor = TimeSpan.FromDays(1));
        Seed(Applied("old", TimeSpan.FromDays(2)));

        var service = host.Services.GetServices<IHostedService>().OfType<DomainEventRetentionService<LibraryContext>>().Should().ContainSingle().Subject;
        var result = await service.DeleteExpiredAsync(Cancellation);

        result.InboxMessages.Should().Be(1);
        InboxConsumers().Should().BeEmpty();
    }

    [Fact]
    public async Task A_window_on_a_table_the_context_does_not_map_fails_with_guidance()
    {
        await using var context = new NoOutboxContext(_db.Options<NoOutboxContext>());
        var retention = new DomainEventRetention<NoOutboxContext>(context, new DomainEventRetentionOptions<NoOutboxContext> { KeepInboxFor = TimeSpan.FromDays(1) });

        var act = () => retention.DeleteExpiredAsync(Cancellation);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*NoOutboxContext*AddDomainEventInbox*KeepInboxFor*");
    }

    [Fact]
    public void Registration_without_either_window_is_refused_because_it_would_delete_nothing()
    {
        var act = () => new ServiceCollection().AddDomainEventRetention<LibraryContext>(_ => { });

        act.Should().Throw<ArgumentException>().WithMessage("*KeepOutboxFor*KeepInboxFor*");
    }

    [Theory]
    [InlineData("KeepOutboxFor")]
    [InlineData("KeepInboxFor")]
    [InlineData("Interval")]
    [InlineData("BatchSize")]
    public void Registration_refuses_a_setting_out_of_range(string setting)
    {
        var act = () => new ServiceCollection().AddDomainEventRetention<LibraryContext>(retention =>
        {
            retention.KeepInboxFor = TimeSpan.FromDays(30);

            switch (setting)
            {
                case "KeepOutboxFor": retention.KeepOutboxFor = TimeSpan.Zero; break;
                case "KeepInboxFor": retention.KeepInboxFor = TimeSpan.FromDays(-1); break;
                case "Interval": retention.Interval = TimeSpan.Zero; break;
                case "BatchSize": retention.BatchSize = 0; break;
            }
        });

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage($"*{setting}*");
    }

    [Fact]
    public void Registering_twice_for_one_context_configures_one_retention_with_one_service()
    {
        var services = new ServiceCollection()
            .AddDomainEventRetention<LibraryContext>(retention => retention.KeepOutboxFor = TimeSpan.FromDays(7))
            .AddDomainEventRetention<LibraryContext>(retention => retention.KeepInboxFor = TimeSpan.FromDays(30));

        services.Count(d => d.ImplementationType == typeof(DomainEventRetentionService<LibraryContext>)).Should().Be(1);

        var options = services.Should().ContainSingle(d => d.ServiceType == typeof(DomainEventRetentionOptions<LibraryContext>))
            .Which.ImplementationInstance.Should().BeOfType<DomainEventRetentionOptions<LibraryContext>>().Subject;
        options.KeepOutboxFor.Should().Be(TimeSpan.FromDays(7));
        options.KeepInboxFor.Should().Be(TimeSpan.FromDays(30));
    }
}
