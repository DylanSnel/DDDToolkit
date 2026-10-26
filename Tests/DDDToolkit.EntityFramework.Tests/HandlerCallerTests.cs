using System.Data.Common;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.EntityFramework.Tests.Domain.Events;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// What the handlers the toolkit calls run as, in a host that requires explicit callers: never the system
/// caller the toolkit's own bookkeeping runs as, unless the module says so where it registers them.
/// </summary>
public sealed class HandlerCallerTests(ExplicitCallersPostgres postgres)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Under_explicit_callers_a_handler_without_a_scope_throws()
    {
        var commands = new CommandCount();
        await using var work = await ToolkitWork.StartAsync(
            await postgres.CreateDatabaseAsync(Cancellation),
            module => module.Handle<ShelfOpenedV3, ReceiptLog>(),
            services: services => services.ConfigureDbContext<CallerInboxContext>(options => options.AddInterceptors(commands)));
        await work.QueueAsync();

        (await work.ProcessAsync()).Should().Be(0);

        var row = (await work.OutboxAsync()).Single();
        row.ProcessedAt.Should().BeNull("the message is tried again once the module says what its handlers run as");
        row.Attempts.Should().Be(1);
        row.LastError.Should().Contain("The handlers of CallerInboxContext have no caller")
            .And.Contain("callers.receipt-log did not run")
            .And.Contain("module.Around(IntegrationEventScopes.System)");
        work.Seen.Callers.Should().BeEmpty();
        commands.Count.Should().Be(0, "the inbox was not asked anything");
        (await work.InboxAsync()).Should().BeEmpty();
        (await work.ReceiptsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsSystem_must_be_explicit()
    {
        // Said where the handlers are registered: the system, the login role, past row level security.
        await using (var system = await DeliverOnceAsync(IntegrationEventScopes.System))
        {
            system.Seen.Callers.Should().Equal([Caller.System]);
            (await system.InboxAsync()).Should().ContainSingle().Which.By.Should().Be("postgres");
        }

        // A scope that begins nothing: the handler has no caller, and the inbox's first read fails.
        await using (var nothing = await DeliverOnceAsync(static (_, _, _) => null))
        {
            nothing.Seen.Callers.Should().BeEmpty();
            (await nothing.OutboxAsync()).Single().LastError.Should().Contain(NoCallerException.DefaultMessage);
            (await nothing.InboxAsync()).Should().BeEmpty();
        }

        // A scoped system caller is not the system: it runs as the role the policies hold.
        await using (var scoped = await DeliverOnceAsync(static (_, _, _) => Callers.Begin(Caller.SystemIn(ToolkitWork.ReceivingScope))))
        {
            scoped.Seen.Callers.Should().ContainSingle().Which.Should().NotBe(Caller.System).And.Match<Caller>(caller => caller.IsSystemIn);
            (await scoped.InboxAsync()).Should().ContainSingle().Which.By.Should().Be(ExplicitCallersPostgres.SystemInRole);
        }
    }

    [Fact]
    public async Task An_in_process_handler_runs_with_no_caller()
    {
        Caller? seen = Caller.Anonymous;
        Exception? asked = null;
        Exception? read = null;

        await using var work = await ToolkitWork.StartAsync(
            await postgres.CreateDatabaseAsync(Cancellation),
            sendToModules: false,
            configure: options => options.DispatchInProcess(async (services, events, cancellationToken) =>
            {
                seen = Callers.Ambient;
                asked = Record(() => services.GetRequiredService<DDDToolkit.Access.ICallerAccessor>().Current);

                try
                {
                    await services.GetRequiredService<CallerInboxContext>().Receipts.CountAsync(cancellationToken);
                }
                catch (NoCallerException exception)
                {
                    read = exception;
                }
            }));
        await work.QueueAsync();

        (await work.ProcessAsync()).Should().Be(1, "the processor read, delivered and marked the row as the system");

        seen.Should().BeNull("the handler does not inherit the system caller the processor ran as");
        asked.Should().BeOfType<NoCallerException>();
        read.Should().BeOfType<NoCallerException>("a handler that touches a context without beginning a caller fails at its first command");
        (await work.OutboxAsync()).Single().ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task An_in_process_handler_cannot_leave_its_changes_to_the_processors_save()
    {
        // Staged on the processor's own context and not saved: under explicit callers the processor's save,
        // the system's, would write it past every policy.
        static Task Stage(IServiceProvider services, string text)
        {
            services.GetRequiredService<CallerOutboxContext>().Labels.Add(new Label { Id = Guid.CreateVersion7(), Text = text });
            return Task.CompletedTask;
        }

        await using (var strict = await ToolkitWork.StartAsync(
            await postgres.CreateDatabaseAsync(Cancellation),
            sendToModules: false,
            configure: options => options.DispatchInProcess((services, events, cancellationToken) => Stage(services, "left behind"))))
        {
            await strict.QueueAsync();

            (await strict.ProcessAsync()).Should().Be(0);

            var row = (await strict.OutboxAsync()).Single();
            row.ProcessedAt.Should().BeNull("the message is tried again");
            row.Attempts.Should().Be(1);
            row.LastError.Should().Contain("left changes to Label on the outbox processor's context without saving them");
            (await strict.LabelsAsync()).Should().BeEmpty("nothing the handler left was saved as the system");
        }

        // A handler that saves its own work, as a caller it begins, is not refused.
        await using (var saving = await ToolkitWork.StartAsync(
            await postgres.CreateDatabaseAsync(Cancellation),
            sendToModules: false,
            configure: options => options.DispatchInProcess(async (services, events, cancellationToken) =>
            {
                using (Callers.Begin(Caller.System))
                {
                    await Stage(services, "saved by the handler");
                    await services.GetRequiredService<CallerOutboxContext>().SaveChangesAsync(cancellationToken);
                }
            })))
        {
            await saving.QueueAsync();

            (await saving.ProcessAsync()).Should().Be(1);
            (await saving.LabelsAsync()).Should().Equal("saved by the handler");
        }

        // Without explicit callers, as in every 3.x host, the change commits with the mark.
        await using (var plain = await ToolkitWork.StartAsync(
            await postgres.CreateDatabaseAsync(Cancellation),
            sendToModules: false,
            requireExplicitCallers: false,
            configure: options => options.DispatchInProcess((services, events, cancellationToken) => Stage(services, "saved with the mark"))))
        {
            await plain.QueueAsync();

            (await plain.ProcessAsync()).Should().Be(1);
            (await plain.LabelsAsync()).Should().Equal("saved with the mark");
        }
    }

    private async Task<ToolkitWork> DeliverOnceAsync(IntegrationEventScope scope)
    {
        var work = await ToolkitWork.StartAsync(
            await postgres.CreateDatabaseAsync(Cancellation),
            module => module.Around(scope).Handle<ShelfOpenedV3, ReceiptLog>());
        await work.QueueAsync();
        await work.ProcessAsync();
        return work;
    }

    private static Exception? Record(Func<Caller> ask)
    {
        try
        {
            ask();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    /// <summary>Counts the commands a context sends.</summary>
    private sealed class CommandCount : DbCommandInterceptor
    {
        private int _count;

        public int Count => _count;

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref _count);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Interlocked.Increment(ref _count);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(result);
        }
    }
}
