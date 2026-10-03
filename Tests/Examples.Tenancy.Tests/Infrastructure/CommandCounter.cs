using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// Counts the statements one flow of work sends to the database, and keeps their text. Added to a module's
/// context options for the whole host, it counts only what is sent from the flow that called
/// <see cref="WatchThisFlow"/>, so the seeder, the outbox pollers and every other test are left out.
/// </summary>
/// <remarks>
/// By flow, not by context: a query reads on a context of its own, which a test could not name beforehand, and
/// the access check before it on another. Counting what the calling flow sends, whichever context sends it,
/// counts all of what a request sent from the test costs, and nothing else.
/// <para>
/// The contexts come from a pool, so one instance serves one reading after another. What tells two readings
/// apart is therefore not the instance but the rental: <see cref="DbContextId"/> names the instance and how many
/// times it has been taken from the pool.
/// </para>
/// </remarks>
public sealed class CommandCounter : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> _commands = new();
    private readonly ConcurrentQueue<DbContext> _contexts = new();
    private readonly ConcurrentQueue<DbContextId> _rentals = new();
    private readonly AsyncLocal<object?> _flow = new();

    /// <summary>The text of every statement counted since the counter was last told to watch, in order.</summary>
    public IReadOnlyList<string> Commands => [.. _commands];

    /// <summary>
    /// Every context instance that sent one of <see cref="Commands"/>, once each, in the order they first did. An
    /// instance the pool handed out twice is here once.
    /// </summary>
    public IReadOnlyList<DbContext> Contexts => [.. _contexts.Distinct()];

    /// <summary>
    /// Every rental of a context that sent one of <see cref="Commands"/>, once each, in the order they first did:
    /// an instance taken from the pool, given back and taken again is two.
    /// </summary>
    public IReadOnlyList<DbContextId> Rentals => [.. _rentals.Distinct()];

    /// <summary>
    /// Counts what this flow of work sends from now on, on any context this counter was added to, and forgets what
    /// was counted before. Everything awaited from here is the same flow; a flow begun elsewhere is not.
    /// </summary>
    public void WatchThisFlow()
    {
        _commands.Clear();
        _contexts.Clear();
        _rentals.Clear();
        _flow.Value = this;
    }

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Count(command, eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Count(command, eventData);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Count(command, eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Count(command, eventData);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Count(command, eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Count(command, eventData);
        return ValueTask.FromResult(result);
    }

    private void Count(DbCommand command, CommandEventData eventData)
    {
        if (_flow.Value is null)
        {
            return;
        }

        _commands.Enqueue(command.CommandText);
        if (eventData.Context is { } context)
        {
            // Read now, while the context is rented: given back, it no longer says which rental this was.
            _contexts.Enqueue(context);
            _rentals.Enqueue(context.ContextId);
        }
    }
}
