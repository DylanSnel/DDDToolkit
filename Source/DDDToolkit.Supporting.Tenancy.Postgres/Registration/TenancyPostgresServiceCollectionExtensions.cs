using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDDToolkit.Supporting.Tenancy.Postgres;

/// <summary>Registers what Tenancy needs on Postgres with row level security.</summary>
public static class TenancyPostgresServiceCollectionExtensions
{
    /// <summary>
    /// Sets <see cref="TenancyRowLevelSecurity.TenantSetting"/> to the current Tenancy caller's tenant whenever a
    /// context under row level security connects, next to the caller's role and claims and in the same
    /// statement; turns the last administrator the trigger keeps at commit, which no index states, into the
    /// refusal the use case gives for the same rule (a slug another tenant has, which the policies hide from the
    /// use case's own check, is refused by its unique index on any database, with no help from here);
    /// requires explicit callers (<see cref="CallerServiceCollectionExtensions.RequireExplicitCallers"/>),
    /// so work nobody said anything about fails rather than running as the login role, past every policy; and
    /// leaves the rights to the database (<see cref="TenancyStoreOptions.DatabaseKeepsRights"/>), whose trigger
    /// writes them and whose policies let a seat read only its own, so the save writes none, the store asks
    /// the database's functions about other seats, and Tenancy's few reads across tenants ask its functions as
    /// scoped system work in no tenant, rather than run as the application itself, past the policies. Call it
    /// next to <c>AddSupabaseRowLevelSecurity</c> or <c>AddPostgresRowLevelSecurity</c>:
    /// <code>
    /// builder.Services.AddSupabaseRowLevelSecurity();
    /// builder.Services.AddTenancyPostgres();
    /// </code>
    /// The policies themselves come with the access files the export writes, from a class of the application's
    /// derived from <see cref="TenancyRowAccessContribution"/>; <see cref="TenancyPostgresChecks"/> checks at
    /// start-up that the database and the host are set up as they rely on. This registers those checks as start-up
    /// checks, which the host runs with <c>services.RunStartupChecks()</c>. Calling it more than once is harmless.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddTenancyPostgres(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RequireExplicitCallers();
        services.AddRowLevelSecuritySettings<TenancyTenantSetting>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITenancySaveFailures, TenancyPostgresFailures>());

        // The policies leave no caller a way to write a right, and show a seat only its own: the database's
        // trigger writes them, and its functions answer what the store asks about other seats, and what is
        // read across tenants.
        services.Configure<TenancyStoreOptions>(options => options.DatabaseKeepsRights = true);

        // That the database and the host are set up as the policies rely on, before the host serves anything, once
        // it runs its checks.
        TenancyPostgresChecks.AddStartupChecks(services);
        return services;
    }
}
