using System.Collections.Concurrent;
using System.Data.Common;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// One thing a context did while somebody held it: a statement it sent, or a save it began.
/// </summary>
/// <param name="Context">The instance. A pool hands one instance to one renter after another.</param>
/// <param name="Rental">The instance and how many times it had been taken from the pool then: which rental this was.</param>
/// <param name="Pooled">Whether the context came from a pool.</param>
/// <param name="Statement">The statement's text; <see langword="null"/> for a save.</param>
/// <param name="Caller">The toolkit's caller of the flow of work it ran in, if any was begun.</param>
public sealed record RentedUse(DbContext Context, DbContextId Rental, bool Pooled, string? Statement, Caller? Caller)
{
    /// <summary>Whether this is a save that began, rather than a statement.</summary>
    public bool IsSave => Statement is null;
}

/// <summary>
/// Keeps what the contexts it is added to did, whoever held them: every request, the outbox pollers and the
/// seeding alike. It is how a test sees which instance served whom, and that a pool handed one out again.
/// </summary>
/// <remarks>
/// Everything is read at the moment it happens. A context given back to its pool answers nothing about itself,
/// and the next renter's rental is another: <see cref="DbContextId"/> names the instance and counts how many
/// times it has been taken from the pool.
/// </remarks>
public sealed class RentalLog : DbCommandInterceptor, ISaveChangesInterceptor
{
    private readonly ConcurrentQueue<RentedUse> _uses = new();

    /// <summary>Everything kept so far, in order.</summary>
    public IReadOnlyList<RentedUse> Uses => [.. _uses];

    /// <summary>Every instance that did something, once each.</summary>
    public IReadOnlyList<DbContext> Instances => [.. _uses.Select(use => use.Context).Distinct()];

    /// <summary>Every rental in which a context did something, once each.</summary>
    public IReadOnlyList<DbContextId> Rentals => [.. _uses.Select(use => use.Rental).Distinct()];

    /// <summary>Forgets what was kept so far.</summary>
    public void Clear() => _uses.Clear();

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Keep(eventData.Context, command.CommandText);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Keep(eventData.Context, command.CommandText);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Keep(eventData.Context, command.CommandText);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Keep(eventData.Context, command.CommandText);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Keep(eventData.Context, command.CommandText);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Keep(eventData.Context, command.CommandText);
        return result;
    }

    /// <inheritdoc />
    public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Keep(eventData.Context, statement: null);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Keep(eventData.Context, statement: null);
        return result;
    }

    private void Keep(DbContext? context, string? statement)
    {
        if (context is not null)
        {
            _uses.Enqueue(new RentedUse(context, context.ContextId, context.IsPooled(), statement, Callers.Ambient));
        }
    }
}
