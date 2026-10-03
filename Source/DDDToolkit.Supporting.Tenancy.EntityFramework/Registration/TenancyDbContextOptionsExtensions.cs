using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>Adds Tenancy's part of every save to a context.</summary>
public static class TenancyDbContextOptionsExtensions
{
    /// <summary>
    /// Adds the <see cref="TenancySaveInterceptor"/> that <c>AddTenancy</c> registered. Use it on Tenancy's own
    /// context and on every context that calls <c>ScopeToTenant</c> or maps the read model, after
    /// <c>UseDDDToolkit</c>:
    /// <code>
    /// services.AddDbContext&lt;ProjectsContext&gt;((serviceProvider, options) =&gt; options
    ///     .UseNpgsql(connectionString)
    ///     .UseDDDToolkit(serviceProvider)
    ///     .UseTenancy(serviceProvider));
    /// </code>
    /// The order matters: after <c>UseDDDToolkit</c>, the save check sees what the domain event handlers
    /// changed, and only aggregates that passed their invariants. <see cref="TenancyChecks.EnsureWired"/>
    /// refuses a context that has it the other way round, or not at all.
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

        return options.AddInterceptors(interceptor);
    }
}
