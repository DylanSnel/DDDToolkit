using DDDToolkit.EntityFramework.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>The two ways Entity Framework registers a context pool.</summary>
public enum PooledAs
{
    /// <summary><c>AddPooledDbContextFactory</c>: a factory that rents, and a scoped context taken from it.</summary>
    Factory,

    /// <summary><c>AddDbContextPool</c>: a scoped context only, rented for the scope and given back with it.</summary>
    ScopedLease,
}

/// <summary>
/// <see cref="TestHost"/>'s shape with <see cref="LibraryContext"/> taken from a context pool: the pool
/// registration <see cref="PooledAs"/> names, with <c>UseDDDToolkit</c> in its options callback, and
/// <c>AddScopedFromPool</c> for the context a scope asks for. Events go to the <see cref="EventRecorder"/> in
/// process, or to an outbox when the test configures one. Every provider validates scopes, so anything that
/// resolves a scoped service from the root fails here as it would in a host.
/// </summary>
public sealed class PooledTestHost : IDisposable
{
    private readonly ServiceProvider _provider;

    public PooledTestHost(
        SqliteDatabase database,
        PooledAs pooledAs,
        Action<DDDEntityFrameworkOptions>? configure = null,
        bool dispatchThroughRecorder = true,
        Action<IServiceCollection>? services = null)
    {
        Database = database;
        PooledAs = pooledAs;
        Recorder = new EventRecorder();

        var collection = new ServiceCollection();
        collection.AddSingleton(Recorder);
        collection.AddDDDToolkitEntityFramework(options =>
        {
            if (dispatchThroughRecorder)
            {
                options.DispatchInProcess((sp, events, ct) => sp.GetRequiredService<EventRecorder>().DispatchAsync(sp, events, ct));
            }

            configure?.Invoke(options);
        });
        AddPool<LibraryContext>(collection, database, pooledAs);
        collection.AddScopedFromPool<LibraryContext>();
        services?.Invoke(collection);

        _provider = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        database.EnsureCreated(() => database.CreateLibraryContext());
    }

    public SqliteDatabase Database { get; }

    public PooledAs PooledAs { get; }

    public EventRecorder Recorder { get; }

    /// <summary>
    /// The factory that rents from the pool with no scope. Only <see cref="PooledAs.Factory"/> has one: a
    /// scoped lease is rented through a scope and no other way.
    /// </summary>
    public IDbContextFactory<LibraryContext> Factory
        => PooledAs == PooledAs.Factory
            ? _provider.GetRequiredService<IDbContextFactory<LibraryContext>>()
            : throw new InvalidOperationException($"{nameof(PooledAs.ScopedLease)} registers no factory; rent through a scope.");

    /// <summary>A new scope; dispose it to give its context back.</summary>
    public IServiceScope CreateScope() => _provider.CreateScope();

    /// <summary>Runs <paramref name="action"/> against the context of a fresh scope, which gives it back afterwards.</summary>
    public async Task<T> InScopeAsync<T>(Func<LibraryContext, IServiceProvider, Task<T>> action)
    {
        using var scope = _provider.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<LibraryContext>(), scope.ServiceProvider);
    }

    public Task InScopeAsync(Func<LibraryContext, Task> action)
        => InScopeAsync<object?>(async (context, _) =>
        {
            await action(context);
            return null;
        });

    /// <summary>
    /// The pool registration <paramref name="pooledAs"/> names for <typeparamref name="TContext"/>, on the
    /// test's SQLite database, with the toolkit's interceptors: what a host writes, and no more.
    /// </summary>
    public static IServiceCollection AddPool<TContext>(IServiceCollection services, SqliteDatabase database, PooledAs pooledAs, Action<IServiceProvider, DbContextOptionsBuilder>? configure = null)
        where TContext : DbContext
    {
        void Options(IServiceProvider provider, DbContextOptionsBuilder options)
        {
            options.UseSqlite(database.Connection).UseDDDToolkit(provider);
            configure?.Invoke(provider, options);
        }

        return pooledAs switch
        {
            PooledAs.Factory => services.AddPooledDbContextFactory<TContext>(Options),
            PooledAs.ScopedLease => services.AddDbContextPool<TContext>(Options),
            _ => throw new ArgumentOutOfRangeException(nameof(pooledAs)),
        };
    }

    public void Dispose() => _provider.Dispose();
}
