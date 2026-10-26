using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// Runs the access questions' queries with Entity Framework's asynchronous operators, and a query that is not
/// Entity Framework's, such as an answer that is a list in memory, with the synchronous ones.
/// </summary>
public sealed class EfQueryExecutor : IQueryExecutor
{
    private EfQueryExecutor()
    {
    }

    /// <summary>The one executor; it keeps no state.</summary>
    public static EfQueryExecutor Instance { get; } = new();

    /// <inheritdoc />
    public Task<bool> AnyAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Provider is IAsyncQueryProvider ? query.AnyAsync(cancellationToken) : Task.FromResult(query.Any());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<T>> ListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Provider is IAsyncQueryProvider
            ? await query.ToListAsync(cancellationToken).ConfigureAwait(false)
            : query.ToList();
    }

    /// <inheritdoc />
    public Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Provider is IAsyncQueryProvider ? query.FirstOrDefaultAsync(cancellationToken)! : Task.FromResult(query.FirstOrDefault());
    }
}
