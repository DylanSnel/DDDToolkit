using DDDToolkit.Access;
using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.Mediator.Tests.Requests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Mediator.Tests.Infrastructure;

/// <summary>
/// A minimal application container on one in-memory SQLite database: Mediator with scoped handlers,
/// the DDDToolkit Entity Framework integration dispatching through <c>DispatchWithMediator</c>, and
/// <see cref="BasketContext"/> registered with <c>UseDDDToolkit</c>. This is what a real host does in
/// Program.cs, minus the web server.
/// <para>
/// The SQLite connection stays open for the lifetime of the host, because closing it drops the
/// in-memory database, and every scope gets a fresh context so read-backs come from the database
/// rather than from a cached entity.
/// </para>
/// <para>
/// The requests about a basket are held to what they declare by the access behavior the toolkit's generator writes
/// into this project, next to a behavior of the host's own that is listed for Mediator's generator.
/// </para>
/// <para>
/// With <c>pooled</c> the context comes from a context pool instead: <c>AddPooledDbContextFactory</c> with
/// the same options callback, and <c>AddScopedFromPool</c> for the context a scope asks for.
/// </para>
/// </summary>
public sealed class TestHost : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public TestHost(Action<DDDEntityFrameworkOptions>? configure = null, bool pooled = false)
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddSingleton(Log);
        services.AddSingleton(StepLog);

        // Scoped, not the Singleton default: BasketEmptiedHandler injects the BasketContext, and a
        // singleton handler cannot take a scoped dependency. The source generator reads this call at
        // compile time, so the lifetime has to be written here rather than configured later.
        //
        // The behaviors listed here are the ones Mediator's generator registers. It reads the list when this
        // project compiles, from the code as its author wrote it, so only a behavior somebody wrote can be on
        // it: naming the one the toolkit's generator writes, BasketAccessBehavior<,>, is its error MSG0007.
        services.AddMediator(options =>
        {
            options.ServiceLifetime = ServiceLifetime.Scoped;
            options.PipelineBehaviors = [typeof(Listed<,>)];
        });

        // The module's check, and the behavior the toolkit wrote for IBasketRequest, added to the container by
        // the registration written with it. After AddMediator, so it runs after the listed behaviors.
        services.AddAccessCheck<IBasketRequest, BasketAccessCheck>();
        services.AddBasketAccessBehavior();

        services.AddDDDToolkitEntityFramework(options =>
        {
            options.DispatchWithMediator();
            configure?.Invoke(options);
        });

        void Options(IServiceProvider provider, DbContextOptionsBuilder options) => options
            .UseSqlite(_connection)
            .UseDDDToolkit(provider);

        if (pooled)
        {
            // The callback runs once, with the root provider; the scope a context is rented in is what
            // the handlers get, and with it the scope's own Mediator.
            services.AddPooledDbContextFactory<BasketContext>(Options);
            services.AddScopedFromPool<BasketContext>();
        }
        else
        {
            services.AddDbContext<BasketContext>(Options);
        }

        services.AddOutboxProcessor<BasketContext>();

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<BasketContext>().Database.EnsureCreated();
    }

    public EventLog Log { get; } = new();

    /// <summary>What the handlers, the check and the behaviors of the requests about a basket did, in order.</summary>
    public StepLog StepLog { get; } = new();

    public IServiceProvider Services => _provider;

    public IServiceScope CreateScope() => _provider.CreateScope();

    /// <summary>Runs <paramref name="action"/> against the context of a fresh scope.</summary>
    public async Task<T> InScopeAsync<T>(Func<BasketContext, IServiceProvider, Task<T>> action)
    {
        using var scope = _provider.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<BasketContext>(), scope.ServiceProvider);
    }

    public Task InScopeAsync(Func<BasketContext, Task> action)
        => InScopeAsync<object?>(async (context, _) =>
        {
            await action(context);
            return null;
        });

    public int CountRows(string table)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }
}
