using DDDToolkit.EntityFramework.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;

/// <summary>
/// A minimal application container over one <see cref="ProviderDatabase"/>: the context registered
/// with <c>AddDbContext</c> plus <c>UseDDDToolkit</c>, which is what puts the interceptors in the
/// pipeline. The interceptors are the part of the toolkit most likely to behave differently on a
/// real server, so the tests that care about them go through a host rather than a bare context.
/// </summary>
public sealed class ProviderHost : IDisposable
{
    private readonly ServiceProvider _provider;

    /// <summary>Builds the container for <paramref name="database"/>.</summary>
    /// <param name="database">The database every scope's context talks to.</param>
    /// <param name="configure">DDDToolkit options, such as the outbox.</param>
    /// <param name="services">Extra registrations, such as the outbox processor.</param>
    public ProviderHost(ProviderDatabase database, Action<DDDEntityFrameworkOptions>? configure = null, Action<IServiceCollection>? services = null)
    {
        ArgumentNullException.ThrowIfNull(database);

        var collection = new ServiceCollection();
        collection.AddDDDToolkitEntityFramework(options => configure?.Invoke(options));
        collection.AddDbContext<ProviderContext>((serviceProvider, builder) =>
        {
            database.Configure(builder);
            builder.UseDDDToolkit(serviceProvider);
        });

        services?.Invoke(collection);

        _provider = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>A new scope; dispose it to dispose the context.</summary>
    public IServiceScope CreateScope() => _provider.CreateScope();

    /// <summary>Runs <paramref name="action"/> against the context of a fresh scope.</summary>
    public async Task<T> InScopeAsync<T>(Func<ProviderContext, IServiceProvider, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        using var scope = _provider.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ProviderContext>(), scope.ServiceProvider);
    }

    /// <summary>Runs <paramref name="action"/> against the context of a fresh scope.</summary>
    public Task InScopeAsync(Func<ProviderContext, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return InScopeAsync<object?>(async (context, _) =>
        {
            await action(context);
            return null;
        });
    }

    /// <inheritdoc />
    public void Dispose() => _provider.Dispose();
}
