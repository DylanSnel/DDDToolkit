using DDDToolkit.EntityFramework.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// A minimal application container: <see cref="LibraryContext"/> registered with
/// <c>AddDbContext</c> + <c>UseDDDToolkit</c>, the DDDToolkit options, and an <see cref="EventRecorder"/>
/// as the in-process dispatch target. Mirrors what a real host does in Program.cs.
/// </summary>
public sealed class TestHost : IDisposable
{
    private readonly ServiceProvider _provider;

    public TestHost(SqliteDatabase database, Action<DDDEntityFrameworkOptions>? configure = null, bool dispatchThroughRecorder = true, Action<IServiceCollection>? services = null)
    {
        Database = database;
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
        collection.AddDbContext<LibraryContext>((sp, options) => options.UseSqlite(database.Connection).UseDDDToolkit(sp));
        services?.Invoke(collection);

        _provider = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<LibraryContext>().Database.EnsureCreated();
    }

    public SqliteDatabase Database { get; }

    public EventRecorder Recorder { get; }

    public IServiceProvider Services => _provider;

    public DDDEntityFrameworkOptions Options => _provider.GetRequiredService<DDDEntityFrameworkOptions>();

    /// <summary>A new scope; dispose it to dispose the context.</summary>
    public IServiceScope CreateScope() => _provider.CreateScope();

    /// <summary>Runs <paramref name="action"/> against the context of a fresh scope.</summary>
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

    public void InScope(Action<LibraryContext> action)
    {
        using var scope = _provider.CreateScope();
        action(scope.ServiceProvider.GetRequiredService<LibraryContext>());
    }

    public void Dispose() => _provider.Dispose();
}
