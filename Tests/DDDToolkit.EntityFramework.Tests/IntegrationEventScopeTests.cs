using System.Data.Common;
using System.Text.Json;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.EntityFramework.Tests.Domain.Events;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// What a module begins around each delivery to its handlers: begun before the inbox is asked anything and
/// ended after its commit, one per handler, nested in the order the module added them.
/// </summary>
public sealed class IntegrationEventScopeTests(ExplicitCallersPostgres postgres) : IDisposable
{
    private readonly SqliteDatabase _db = new();
    private readonly List<string> _steps = [];

    public void Dispose() => _db.Dispose();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_scope_spans_the_inbox_read_the_handler_the_save_and_the_commit()
    {
        var trace = new CallerTrace();
        var handler = new FirstHandler(_steps);
        using var host = Host(module => module.Around(Begins("shipping")).Handle<ShelfOpenedV3>(handler), trace);
        trace.Clear();

        await DeliverAsync(host);

        handler.Seen.Should().Equal("system in shipping");
        trace.Inbox.Should().HaveCount(2, "the inbox reads whether the message was applied, then inserts its row")
            .And.OnlyContain(step => step.Caller == "system in shipping");
        trace.Commits.Should().ContainSingle().Which.Should().Be("system in shipping", "the inbox's transaction commits inside the scope");
        _steps.Should().Equal("begin shipping", "handler system in shipping", "end shipping");
    }

    [Fact]
    public async Task Each_handler_gets_its_own_scope()
    {
        var entered = 0;
        using var host = Host(module => module
            .Around((services, message, contract) => Begins($"scope-{Interlocked.Increment(ref entered)}")(services, message, contract))
            .Handle<ShelfOpenedV3>(new FirstHandler(_steps))
            .Handle<ShelfOpenedV3>(new SecondHandler(_steps)));

        await DeliverAsync(host);

        _steps.Should().Equal(
            "begin scope-1", "handler system in scope-1", "end scope-1",
            "begin scope-2", "second system in scope-2", "end scope-2");
    }

    [Fact]
    public async Task The_scope_is_disposed_when_the_handler_fails()
    {
        using var host = Host(module => module.Around(Begins("shipping")).Handle<ShelfOpenedV3>(new FirstHandler(_steps) { Refuse = true }));

        var deliver = () => DeliverAsync(host);

        await deliver.Should().ThrowAsync<IntegrationEventDeliveryException>();
        _steps.Should().Equal("begin shipping", "handler system in shipping", "end shipping");
        _db.CountRows("InboxMessages").Should().Be(0);
    }

    [Fact]
    public async Task Scopes_nest_in_the_order_they_were_added()
    {
        using var host = Host(module => module
            .Around(Begins("outer"))
            .Around(Begins("inner"))
            .Handle<ShelfOpenedV3>(new FirstHandler(_steps)));

        await DeliverAsync(host);

        _steps.Should().Equal("begin outer", "begin inner", "handler system in inner", "end inner", "end outer");
    }

    /// <summary>
    /// On Postgres with row level security: the handler's rows and the inbox row are written as the scoped system
    /// caller the module's scope began, which the receipts' policy asks by its scope; a scope the policy does
    /// not know is refused, and takes the inbox row down with the handler's work.
    /// </summary>
    [Fact]
    public async Task A_handler_under_a_scoped_system_caller_saves_its_rows_and_the_inbox_row_as_that_caller()
    {
        var claims = $$"""{"role":"{{ExplicitCallersPostgres.SystemInRole}}","scope":"{{ToolkitWork.ReceivingScope}}"}""";

        await using (var work = await ToolkitWork.StartAsync(
            await postgres.CreateDatabaseAsync(Cancellation),
            module => module
                .Around((_, _, _) => Callers.Begin(Caller.SystemIn(ToolkitWork.ReceivingScope)))
                .Handle<ShelfOpenedV3, ReceiptLog>(),
            requireExplicitCallers: false))
        {
            await work.QueueAsync("Fiction");

            (await work.ProcessAsync()).Should().Be(1);

            (await work.ReceiptsAsync()).Should().Equal(new Written("Fiction", ExplicitCallersPostgres.SystemInRole, claims));
            (await work.InboxAsync()).Should().Equal(new Written("callers.receipt-log", ExplicitCallersPostgres.SystemInRole, claims));
        }

        await using (var elsewhere = await ToolkitWork.StartAsync(
            await postgres.CreateDatabaseAsync(Cancellation),
            module => module
                .Around((_, _, _) => Callers.Begin(Caller.SystemIn("elsewhere")))
                .Handle<ShelfOpenedV3, ReceiptLog>(),
            services: services => services.AddSingleton<KeptLogs>().AddLogging(logging => logging.Services.AddSingleton<ILoggerProvider>(provider => provider.GetRequiredService<KeptLogs>())),
            requireExplicitCallers: false))
        {
            await elsewhere.QueueAsync("Fiction");

            (await elsewhere.ProcessAsync()).Should().Be(0);

            (await elsewhere.OutboxAsync()).Single().LastError.Should().Contain("CallerInboxContext/callers.receipt-log");
            elsewhere.Services.GetRequiredService<KeptLogs>().Exceptions.OfType<DbUpdateException>()
                .Select(failure => failure.InnerException).OfType<PostgresException>()
                .Should().NotBeEmpty()
                .And.OnlyContain(refusal => refusal.SqlState == PostgresErrorCodes.InsufficientPrivilege, "the receipts are the receiving scope's to write");
            (await elsewhere.ReceiptsAsync()).Should().BeEmpty();
            (await elsewhere.InboxAsync()).Should().BeEmpty("the inbox row goes with the handler's work");
        }
    }

    private TestHost Host(Action<ModuleIntegrationEvents<LibraryContext>> module, CallerTrace? trace = null)
        => new(
            _db,
            options => options.MapIntegrationEvents(contracts => contracts.Register<ShelfOpenedV3>()),
            dispatchThroughRecorder: false,
            services =>
            {
                services.AddModuleIntegrationEvents(module);
                if (trace is not null)
                {
                    services.ConfigureDbContext<LibraryContext>(options => options.AddInterceptors(trace));
                }
            });

    /// <summary>A scope that begins <c>Caller.SystemIn(name)</c> and writes down when it begins and ends.</summary>
    private IntegrationEventScope Begins(string name) => (_, _, _) =>
    {
        _steps.Add($"begin {name}");
        var caller = Callers.Begin(Caller.SystemIn(name));
        return new Ending(() =>
        {
            caller.Dispose();
            _steps.Add($"end {name}");
        });
    };

    /// <summary>One delivery through the module sink, in a scope of its own, as the outbox processor or a transport makes one.</summary>
    private static async Task DeliverAsync(TestHost host)
    {
        using var scope = host.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ModuleIntegrationEventSink>().SendAsync(new IntegrationEventMessage
        {
            MessageId = Guid.CreateVersion7(),
            Name = "library.shelf-opened",
            Version = 3,
            Payload = JsonSerializer.Serialize(new ShelfOpenedV3("SHELF_1", "Fiction")),
            OccurredAt = DateTimeOffset.UtcNow,
        }, Cancellation);
    }

    private sealed class Ending(Action end) : IDisposable
    {
        public void Dispose() => end();
    }

    [IntegrationEventConsumer("scopes.first")]
    private sealed class FirstHandler(List<string> steps) : IIntegrationEventHandler<ShelfOpenedV3>
    {
        public List<string> Seen { get; } = [];

        public bool Refuse { get; init; }

        public Task HandleAsync(ShelfOpenedV3 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
        {
            Seen.Add(Callers.Ambient?.ToString() ?? "nobody");
            steps.Add($"handler {Callers.Ambient}");
            return Refuse ? throw new InvalidOperationException("Refused on purpose.") : Task.CompletedTask;
        }
    }

    [IntegrationEventConsumer("scopes.second")]
    private sealed class SecondHandler(List<string> steps) : IIntegrationEventHandler<ShelfOpenedV3>
    {
        public Task HandleAsync(ShelfOpenedV3 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
        {
            steps.Add($"second {Callers.Ambient}");
            return Task.CompletedTask;
        }
    }

    /// <summary>Who each command on the inbox's table, and each commit, ran under.</summary>
    private sealed class CallerTrace : DbCommandInterceptor, IDbTransactionInterceptor
    {
        private readonly List<(string Command, string? Caller)> _commands = [];
        private readonly List<string?> _commits = [];

        public IReadOnlyList<(string Command, string? Caller)> Inbox => [.. _commands.Where(step => step.Command.Contains("InboxMessages", StringComparison.Ordinal))];

        public IReadOnlyList<string?> Commits => _commits;

        /// <summary>Forgets what ran before the delivery, such as the tables the host made.</summary>
        public void Clear()
        {
            _commands.Clear();
            _commits.Clear();
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Record(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Record(command);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Record(command);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => _commits.Add(Callers.Ambient?.ToString());

        public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            _commits.Add(Callers.Ambient?.ToString());
            return Task.CompletedTask;
        }

        private void Record(DbCommand command) => _commands.Add((command.CommandText, Callers.Ambient?.ToString()));
    }
}
