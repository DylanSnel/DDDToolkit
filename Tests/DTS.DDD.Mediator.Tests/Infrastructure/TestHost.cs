using DTS.DDD.Access;
using DTS.DDD.EntityFramework;
using DTS.DDD.EntityFramework.Options;
using DTS.DDD.Mediator.Tests.Requests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DTS.DDD.Mediator.Tests.Infrastructure;

/// <summary>
/// A minimal application container on one in-memory SQLite database: the toolkit's mediator with the handlers
/// <c>AddMediatorTestsHandlers()</c> registers, the DDDToolkit Entity Framework integration dispatching through
/// <c>DispatchWithMediator</c>, and <see cref="BasketContext"/> registered with <c>UseDDDToolkit</c>. This is what a
/// real host does in Program.cs, minus the web server.
/// <para>
/// The SQLite connection stays open for the lifetime of the host, because closing it drops the
/// in-memory database, and every scope gets a fresh context so read-backs come from the database
/// rather than from a cached entity.
/// </para>
/// <para>
/// The requests about a basket are held to what they declare by the access behavior the toolkit's generator writes
/// into this project, next to a behavior of the host's own.
/// </para>
/// <para>
/// With <c>pooled</c> the context comes from a context pool instead: <c>AddPooledDbContextFactory</c> with
/// the same options callback, and <c>AddScopedFromPool</c> for the context a scope asks for.
/// </para>
/// <para>
/// Without <c>accessBehavior</c> the module's check is registered and the behavior that asks it is not: the host a
/// developer composes who forgot the line, which the start-up check of the behaviors refuses.
/// </para>
/// <para>
/// <c>register</c> adds what one test needs after the generated registration, such as a handler of its own or a
/// notification behavior.
/// </para>
/// </summary>
public sealed class TestHost : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public TestHost(
        Action<DDDEntityFrameworkOptions>? configure = null,
        bool pooled = false,
        bool accessBehavior = true,
        Action<IServiceCollection>? register = null)
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = Registrations;
        services.AddSingleton(Log);
        services.AddSingleton(StepLog);

        // Every handler of this project, the notification handlers and the request handlers, written by the
        // toolkit's generator. Each is scoped, so a handler can take the scope's services, as a module's handlers do.
        services.AddMediatorTestsHandlers();
        register?.Invoke(services);

        // A behavior of the host's own, in front of every request, command and query.
        services.AddPipelineBehavior(typeof(Listed<,>));

        // The second module's behavior, registered closed over its one command, as a host may register a module's
        // behaviors message by message. The module has no query answered with a stream, so its behavior for streams
        // is registered over nothing.
        services.AddPipelineBehavior(typeof(StockAccess<TakeStock, Unit>));

        // The module's check, and the behavior the toolkit wrote for IBasketRequest, added to the container by
        // the registration written with it. After the host's behavior, so it runs inside it.
        services.AddAccessCheck<IBasketRequest, BasketAccessCheck>();
        if (accessBehavior)
        {
            services.AddBasketAccessBehavior();
        }

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
            // the handlers get, and with it the scope's own mediator.
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

    /// <summary>What the host registered, as a start-up check reads it.</summary>
    public IServiceCollection Registrations { get; } = new ServiceCollection();

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
