namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Runs a query asynchronously without the package knowing the storage. Entity Framework's asynchronous
/// operators live in Entity Framework, which this package does not reference; the storage that supplies the
/// read source supplies the way to run what is built over it.
/// </summary>
public interface IQueryExecutor
{
    /// <summary>Whether the query has any row.</summary>
    Task<bool> AnyAsync<T>(IQueryable<T> query, CancellationToken cancellationToken);

    /// <summary>Every row of the query.</summary>
    Task<IReadOnlyList<T>> ListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken);

    /// <summary>The first row of the query, or the default when it has none.</summary>
    Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query, CancellationToken cancellationToken);
}
