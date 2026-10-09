using DDDToolkit.EntityFramework.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework;

/// <summary>
/// Contexts taken from Entity Framework's context pool, used with the toolkit.
/// <code>
/// services.AddPooledDbContextFactory&lt;OrderingContext&gt;((provider, options) => options
///     .UseNpgsql(connectionString)
///     .UseDDDToolkit(provider));
/// services.AddScopedFromPool&lt;OrderingContext&gt;();
/// </code>
/// <para>
/// A pool builds its options once, with the application's root services, and every context of the pool
/// shares them. So nothing in the options callback may read a caller, a request or a scope, and the
/// toolkit's own <c>Use…(provider)</c> methods resolve singletons only. What differs per use, who is
/// calling and which scope the handlers of a save belong to, is read when it is used.
/// </para>
/// </summary>
public static class PooledContexts
{
    /// <summary>
    /// Makes the <typeparamref name="TContext"/> a scope asks for one taken from its context pool and bound
    /// to that scope (<see cref="BindToScope{TContext}"/>): rented when the scope first asks for it and given back when
    /// the scope ends. Call it after <c>AddPooledDbContextFactory&lt;TContext&gt;(...)</c> or
    /// <c>AddDbContextPool&lt;TContext&gt;(...)</c>. Calling it again does nothing.
    /// <para>
    /// Entity Framework 10 registers <typeparamref name="TContext"/> as a scoped service for both pool
    /// registrations already, and gives it back to the pool with the scope; that registration is kept and
    /// bound. Where there is only a factory, one a host registered by hand for example,
    /// <typeparamref name="TContext"/> is registered over it: the scope's context is the factory's, and the
    /// scope disposes it, once, which gives it back.
    /// </para>
    /// <para>
    /// Without the binding, a context a scope takes from a pool cannot dispatch its domain events in
    /// process: the pool's options know the application's services only, and the toolkit refuses to hand
    /// those to a handler. The outbox needs no scope and no binding.
    /// </para>
    /// </summary>
    /// <typeparam name="TContext">The pooled context.</typeparam>
    /// <param name="services">The application's services.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// No pool is registered for <typeparamref name="TContext"/>, or <typeparamref name="TContext"/> is
    /// registered with <c>AddDbContext</c>. A context registered with <c>AddDbContextFactory</c> is refused
    /// with the same exception when a scope first asks for it: Entity Framework registers it the way it
    /// registers a pooled one, so only the context itself says that it comes from no pool.
    /// </exception>
    public static IServiceCollection AddScopedFromPool<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        if (LastIndexOf(services, typeof(PooledRegistration<TContext>)) >= 0)
        {
            return services;
        }

        var pooled = new PooledRegistration<TContext>();
        var index = LastIndexOf(services, typeof(TContext));

        if (index >= 0)
        {
            // A type registration is AddDbContext's: a context made for the scope, by the scope.
            var make = services[index].ImplementationFactory
                ?? throw new InvalidOperationException(PooledRegistration<TContext>.NotFromAPool);

            // In place, so the context keeps its position among the registrations of its type.
            services[index] = ServiceDescriptor.Scoped(scope => pooled.Bind((TContext)make(scope), scope));
        }
        else if (LastIndexOf(services, typeof(IDbContextFactory<TContext>)) >= 0)
        {
            services.AddScoped(scope => pooled.Bind(scope.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContext(), scope));
        }
        else
        {
            throw new InvalidOperationException(
                $"AddScopedFromPool<{typeof(TContext).Name}>() found no context pool for {typeof(TContext).Name}. " +
                $"Call services.AddPooledDbContextFactory<{typeof(TContext).Name}>(...) or services.AddDbContextPool<{typeof(TContext).Name}>(...) first.");
        }

        services.AddSingleton(pooled);
        return services;
    }

    /// <summary>
    /// Names the scope whose services the in-process handlers of this context's domain events are given, for
    /// as long as the caller holds the context: until it is disposed, which gives a pooled one back. Returns
    /// the context. It names the services only; it does not make this context the scope's own.
    /// <code>
    /// await using var context = (await factory.CreateDbContextAsync(cancellationToken)).BindToScope(scope.ServiceProvider);
    /// </code>
    /// The next renter of a pooled context starts with no scope, whatever this one named. A context that is
    /// not pooled may be bound too, and the scope named here then wins over the provider its options were
    /// built with.
    /// </summary>
    /// <typeparam name="TContext">The context's type.</typeparam>
    /// <param name="context">The context.</param>
    /// <param name="scopedServices">A scope's services, such as <c>scope.ServiceProvider</c> or a request's.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="scopedServices"/> is null.</exception>
    public static TContext BindToScope<TContext>(this TContext context, IServiceProvider scopedServices)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(scopedServices);

        ScopeBinding.Set(context, scopedServices);
        return context;
    }

    /// <summary>
    /// Whether the context's options are a pool's: <c>AddPooledDbContextFactory</c>, <c>AddDbContextPool</c>
    /// and <c>PooledDbContextFactory&lt;TContext&gt;</c> all set <see cref="CoreOptionsExtension.MaxPoolSize"/>,
    /// and nothing else does.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    public static bool IsPooled(this DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.MaxPoolSize is not null;
    }

    /// <summary>The last registration of <paramref name="serviceType"/> that has no key, which is the one a scope resolves; -1 when there is none.</summary>
    private static int LastIndexOf(IServiceCollection services, Type serviceType)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (!services[i].IsKeyedService && services[i].ServiceType == serviceType)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>
/// That <see cref="PooledContexts.AddScopedFromPool{TContext}"/> ran for <typeparamref name="TContext"/>, so a
/// second call does nothing, and the check that what a scope is given really comes from a pool.
/// </summary>
internal sealed class PooledRegistration<TContext>
    where TContext : DbContext
{
    /// <summary>What a context that comes from no pool is refused with, at registration or at the first rental.</summary>
    public static readonly string NotFromAPool =
        $"{typeof(TContext).Name} does not come from a context pool. AddDbContext and AddDbContextFactory give each scope a context of its own already; " +
        "AddScopedFromPool is for a context registered with AddPooledDbContextFactory or AddDbContextPool.";

    /// <summary>Whether a context of this registration was seen to be pooled. The registration decides that, so one look is enough.</summary>
    private volatile bool _pooled;

    /// <summary>Binds the context a scope was just given to that scope.</summary>
    /// <exception cref="InvalidOperationException">The context comes from no pool.</exception>
    public TContext Bind(TContext context, IServiceProvider scope)
    {
        if (!_pooled)
        {
            if (!context.IsPooled())
            {
                // The scope never gets it, so nothing else would dispose it.
                context.Dispose();
                throw new InvalidOperationException(NotFromAPool);
            }

            _pooled = true;
        }

        return context.BindToScope(scope);
    }
}
