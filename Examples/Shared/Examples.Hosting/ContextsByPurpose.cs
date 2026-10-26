using DDDToolkit.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Examples.Hosting;

/// <summary>
/// One factory over two context pools, one per data source of <see cref="PostgresPools"/>: a context rented while
/// the toolkit's system caller is current comes from the background pool, every other from the requests pool.
/// Who is calling is read at each rental, never kept.
/// </summary>
/// <typeparam name="TContext">The context both pools hold.</typeparam>
internal sealed class ContextsByPurpose<TContext> : IDbContextFactory<TContext>
    where TContext : DbContext
{
    private readonly PooledDbContextFactory<TContext> _requests;
    private readonly PooledDbContextFactory<TContext> _background;

    public ContextsByPurpose(DbContextOptions<TContext> requests, DbContextOptions<TContext> background)
    {
        RequestOptions = requests;
        _requests = new PooledDbContextFactory<TContext>(requests);
        _background = new PooledDbContextFactory<TContext>(background);
    }

    /// <summary>The options of the contexts requests are answered on.</summary>
    public DbContextOptions<TContext> RequestOptions { get; }

    /// <inheritdoc />
    public TContext CreateDbContext() => (Callers.Ambient?.IsSystem == true ? _background : _requests).CreateDbContext();
}
