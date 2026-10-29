namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Runs a query asynchronously without the package knowing the storage. Entity Framework's asynchronous
/// operators live in Entity Framework, which this package does not reference; the storage that supplies the
/// read source supplies the way to run what is built over it.
/// </summary>
public interface IQueryExecutor
{
    /// <summary>Whether the query has any row.</summary>
    /// <typeparam name="T">The type of the query's rows.</typeparam>
    /// <param name="query">The query, of the read source's storage.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<bool> AnyAsync<T>(IQueryable<T> query, CancellationToken cancellationToken);

    /// <summary>Every row of the query.</summary>
    /// <typeparam name="T">The type of the query's rows.</typeparam>
    /// <param name="query">The query, of the read source's storage.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyList<T>> ListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken);

    /// <summary>The first row of the query, or the default when it has none.</summary>
    /// <typeparam name="T">The type of the query's rows.</typeparam>
    /// <param name="query">The query, of the read source's storage.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query, CancellationToken cancellationToken);
}
