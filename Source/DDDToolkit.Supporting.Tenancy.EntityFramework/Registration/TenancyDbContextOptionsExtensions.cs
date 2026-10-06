using DDDToolkit.Composition;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>Adds Tenancy's part of every save to a context.</summary>
public static class TenancyDbContextOptionsExtensions
{
    /// <summary>
    /// Adds the <see cref="TenancySaveInterceptor"/> that <c>AddTenancy</c> registered. <c>UseDDDToolkit</c> calls it
    /// for every context once Tenancy is registered, so a context wired with that one call needs nothing more:
    /// <code>
    /// services.AddDbContext&lt;ProjectsContext&gt;((serviceProvider, options) =&gt; options
    ///     .UseNpgsql(connectionString)
    ///     .UseDDDToolkit(serviceProvider));
    /// </code>
    /// The interceptor goes on every context the one call wires, Tenancy's own and a module's alike: the options do
    /// not know the model yet, and the interceptor does, at every save. It checks the saves of a context whose model
    /// keeps rows to a tenant, Tenancy's own or those of <c>ScopeToTenant</c>, writes the rights where the model maps
    /// Tenancy's own classes, and does nothing where the model has neither.
    /// <para>
    /// Call it yourself after <c>UseDDDToolkitCore</c>, for a context that takes the parts it wants one by one, and
    /// last: after the toolkit's interceptors, the save check sees what the domain event handlers changed, and only
    /// aggregates that passed their invariants; and a row an interceptor after it added would be saved unchecked. <see cref="TenancyChecks.EnsureWired"/> refuses a context
    /// that has it the other way round, or not at all. It adds nothing to options that already have the
    /// interceptor, so a chain that calls it after <c>UseDDDToolkit</c> gives it once.
    /// </para>
    /// <para>
    /// The same call serves a context pool (<c>AddPooledDbContextFactory</c>, <c>AddDbContextPool</c>): the save
    /// check reads the caller of each save, not of the options, so a context taken from a pool checks each
    /// renter's saves against that renter's tenant.
    /// </para>
    /// </summary>
    /// <param name="options">The context's options.</param>
    /// <param name="services">
    /// The provider handed to the options callback, of <c>AddDbContext</c> or of a context pool: the interceptor
    /// is one instance for the application, so the root provider a pool hands its callback serves as well as a
    /// scope's.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or <paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Tenancy is not registered in <paramref name="services"/>.</exception>
    public static DbContextOptionsBuilder UseTenancy(this DbContextOptionsBuilder options, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(services);

        var interceptor = services.GetService<TenancySaveInterceptor>()
            ?? throw new InvalidOperationException(
                "UseTenancy found no TenancySaveInterceptor. Register Tenancy first, with services.AddTenancy<…, TContext>(…) of DDDToolkit.Supporting.Tenancy.EntityFramework.");

        var present = options.Options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];
        return present.OfType<TenancySaveInterceptor>().Any() ? options : options.AddInterceptors(interceptor);
    }

    /// <summary>Registers the part <see cref="TenancySaveInterceptor.PartName"/>, once however many times Tenancy is registered.</summary>
    internal static void AddPart(IServiceCollection services)
        => services.AddContextPart(new ContextPart<DbContextOptionsBuilder>(
            TenancySaveInterceptor.PartName,
            TenancySaveInterceptor.PartPosition,
            (options, provider) => options.UseTenancy(provider)));
}
