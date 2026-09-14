using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Options;
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
/// </summary>
public sealed class TestHost : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public TestHost(Action<DDDEntityFrameworkOptions>? configure = null)
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddSingleton(Log);

        // Scoped, not the Singleton default: BasketEmptiedHandler injects the BasketContext, and a
        // singleton handler cannot take a scoped dependency. The source generator reads this call at
        // compile time, so the lifetime has to be written here rather than configured later.
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);

        services.AddDDDToolkitEntityFramework(options =>
        {
            options.DispatchWithMediator();
            configure?.Invoke(options);
        });

        services.AddDbContext<BasketContext>((provider, options) => options
            .UseSqlite(_connection)
            .UseDDDToolkit(provider));

        services.AddOutboxProcessor<BasketContext>();

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<BasketContext>().Database.EnsureCreated();
    }

    public EventLog Log { get; } = new();

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
