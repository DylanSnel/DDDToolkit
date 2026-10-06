using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.Startup;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// Checks an application can run against its Tenancy storage: that a context is wired so its saves are
/// checked, and which keys stored on roles the catalogue no longer knows.
/// <para>
/// <c>AddTenancy</c> registers them as start-up checks, by the names below, which a host runs with
/// <c>services.RunStartupChecks()</c>: the catalogue builds, every registered context is wired as
/// <see cref="EnsureWired"/> asks, and every key a role holds that the catalogue does not know is logged as a
/// warning. The methods stay for a host that runs them by hand.
/// </para>
/// </summary>
public static class TenancyChecks
{
    /// <summary>
    /// The start-up check that the catalogue builds: asked for the first time at start-up, so a catalogue that does
    /// not hold together, two modules that declare one key say, stops the start rather than the first request that
    /// needs it. Every module has added its keys by then, whatever order they were registered in.
    /// </summary>
    public const string CatalogueBuildsCheck = "tenancy.catalogue-builds";

    /// <summary>
    /// The start-up check that every registered context that keeps rows to a tenant checks its saves
    /// (<see cref="EnsureWired"/>). Every context the container knows, so a module added later is checked without
    /// anyone remembering to add it; one without such rows passes as it is.
    /// </summary>
    public const string ContextsWiredCheck = "tenancy.contexts-wired";

    /// <summary>
    /// The start-up check that logs, as a warning, every key a role holds that the catalogue does not know
    /// (<see cref="UnknownStoredKeysAsync"/>): removed from the code rather than retired. Such a key gives nothing
    /// and does no harm, so it refuses no start. It reads what is stored, so it runs with the checks of the
    /// database, and after those of Tenancy's own that prove it may read it.
    /// </summary>
    public const string UnknownStoredKeysCheck = "tenancy.unknown-stored-keys";

    /// <summary>
    /// Throws when <paramref name="context"/> has entity types kept to a tenant, Tenancy's own or those of
    /// <c>ScopeToTenant</c>, and its saves would not be checked: the context was configured with
    /// <c>UseDDDToolkitCore</c> and no <c>UseTenancy</c>, or without the toolkit, or <c>UseTenancy</c> was called
    /// before the toolkit's interceptors, or Tenancy is not registered at all, so <c>UseDDDToolkit</c> had no save
    /// check to add. <c>UseDDDToolkit</c> adds it in its place once <c>AddTenancy</c> is registered. The store calls
    /// it before every save; call it at start-up, or before the save of a unit of work of your own, for every other
    /// context that keeps entities to a tenant. Without it, a context that keeps rows to a tenant is still refused
    /// at its first save where it lacks the save check, since its model requires it
    /// (<c>ContextPartRequirements</c>), and by the start-up check <c>entity-framework.toolkit-wired</c>.
    /// <para>
    /// Without it a context without the save check is silent: rows of another tenant are written and the stored
    /// rights are not. A save check added too early would check the rows before the domain event handlers changed
    /// them, and before the invariants had refused the save.
    /// </para>
    /// <para>
    /// A context that keeps Tenancy's access history (<c>AddTenancyEventLogTable</c>) is refused as well when the
    /// accessor that says who acted is not Tenancy's: <c>AddTenancy</c> puts <see cref="TenancyActedByAccessor"/>
    /// around the accessor registered before it, and one registered after it takes its place. Every row of the
    /// history would then name the signed-in identity where it should name the seat, and on Postgres the
    /// history's policies would refuse a seat's save.
    /// </para>
    /// <para>
    /// What the model holds is worked out once per model; the interceptors are read from the context's
    /// options each time, because two contexts of one type can be given different options.
    /// </para>
    /// </summary>
    /// <param name="context">The context to check.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The context's saves would not be checked, or would be checked too early; or the context keeps the access
    /// history and another accessor than Tenancy's says who acted.
    /// </exception>
    public static void EnsureWired(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var facts = TenancyModelFacts.Of(context.Model);
        if (!facts.HasScopedTypes)
        {
            return;
        }

        var interceptors = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.Interceptors?.ToList() ?? [];
        var invariants = IndexOf<InvariantInterceptor>(interceptors);
        var tenancy = IndexOf<TenancySaveInterceptor>(interceptors);

        if (invariants >= 0 && tenancy > invariants)
        {
            if (facts.KeepsAccessHistory && ((TenancySaveInterceptor)interceptors[tenancy]).ProblemWithWhoActed is { } whoActed)
            {
                throw new InvalidOperationException(
                    "'" + context.GetType().Name + "' keeps Tenancy's access history, and its rows would not say who acted as Tenancy knows the caller: " + whoActed);
            }

            return;
        }

        var problem = tenancy < 0
            ? "has no TenancySaveInterceptor"
            : invariants < 0
                ? "has no DDDToolkit interceptors"
                : "adds the TenancySaveInterceptor before the DDDToolkit interceptors";

        // Where the application's services say Tenancy is not registered, the one call had no save check to add.
        var services = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider;
        if (tenancy < 0 && services is not null && services.GetService<TenancySaveInterceptor>() is null)
        {
            throw new InvalidOperationException(
                "'" + context.GetType().Name + "' keeps entities to a tenant but " + problem + ", so its saves are not checked as they should be: Tenancy is not registered, "
                + "so UseDDDToolkit had no save check to add. Register it with services.AddTenancy<…, TContext>(…) of DDDToolkit.Supporting.Tenancy.EntityFramework.");
        }

        throw new InvalidOperationException(
            "'" + context.GetType().Name + "' keeps entities to a tenant but " + problem + ", so its saves are not checked as they should be. "
            + "Configure it with options.UseDDDToolkit(serviceProvider), which adds Tenancy's save check, last, once services.AddTenancy<…, TContext>(…) registered it; "
            + "after options.UseDDDToolkitCore(serviceProvider), with options.UseTenancy(serviceProvider).");
    }

    /// <summary>
    /// The keys stored on roles, in every tenant, that <paramref name="catalogue"/> does not know: removed from
    /// the code rather than retired. Such a key gives no rights, and may stay on its role; this names them, so
    /// a start-up check can report them. The query reads the roles' keys and nothing else, past every filter:
    /// a role an application's own filter hides still holds its keys.
    /// <para>
    /// It is one of the few reads across tenants Tenancy makes (<see cref="TenancySystemReads"/>): one query, on
    /// a context of its own made in a new scope of the application's services, as a caller begun around it
    /// alone, so neither the caller of this method nor a transaction open on <paramref name="context"/> decides
    /// who it runs as. Where the database keeps the rights (<see cref="TenancyStoreOptions.DatabaseKeepsRights"/>,
    /// as on Postgres with row level security), that caller is Tenancy's scoped system work in no tenant, a role
    /// the policies hold, and the query asks the database's function
    /// <see cref="TenancyFunctionNames.RoleKeysInUse"/>, which answers the keys and nothing else of any role.
    /// Anywhere else it is the toolkit's <c>Caller.System</c>, the application itself, reading the roles' table.
    /// </para>
    /// </summary>
    /// <param name="context">
    /// A context of Tenancy's, resolved from the application's services: it names the context type and the
    /// services the read makes its own context from, and is not queried itself.
    /// </param>
    /// <param name="catalogue">The catalogue the application runs with.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The unknown keys, each once, in ordinal order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="catalogue"/> is null.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="context"/> was not made by the application's services.</exception>
    public static async Task<IReadOnlyList<string>> UnknownStoredKeysAsync<TRole, TRoleId, TTenantId>(
        DbContext context,
        TenancyCatalogue catalogue,
        CancellationToken cancellationToken)
        where TRole : RoleAggregate<TRoleId, TTenantId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(catalogue);

        // Every tenant's roles, so past the filters, and only their keys, so no tenant's data leaves the query but
        // key strings. Over the tables, the keys are flattened here rather than in SQL: a JSON column on one
        // provider is an array on another. The database's function answers them flat already, whichever it is.
        var stored = await TenancySystemReads.RunAsync<IReadOnlyList<string>>(
            context,
            TenancyWork.SystemScope,
            overTables: async (own, cancellation) =>
            {
                var held = await own.Set<TRole>()
                    .IgnoreQueryFilters()
                    .Select(role => role.Keys)
                    .ToListAsync(cancellation)
                    .ConfigureAwait(false);
                return [.. held.SelectMany(keys => keys)];
            },
            throughFunctions: async (own, cancellation) => await own.Database
                .SqlQueryRaw<string>(TenancyFunctionSql.SelectValues(own, schema: null, TenancyFunctionNames.RoleKeysInUse, parameters: 0, "Value"))
                .ToListAsync(cancellation)
                .ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        return stored
            .Where(key => !catalogue.Knows(key))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Registers the three checks above as start-up checks, the last over <typeparamref name="TContext"/>, the
    /// context whose model maps Tenancy's tables: once, however many times Tenancy is registered.
    /// </summary>
    internal static void AddStartupChecks<TRole, TRoleId, TTenantId, TContext>(IServiceCollection services)
        where TRole : RoleAggregate<TRoleId, TTenantId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TContext : DbContext
    {
        services.AddStartupCheck(new StartupCheck(CatalogueBuildsCheck, StartupCheckStage.Services, static (provider, cancellationToken) =>
        {
            // Built the first time it is asked for, here; a catalogue that does not hold together throws.
            provider.GetRequiredService<TenancyCatalogue>();
            return Task.CompletedTask;
        }));

        services.AddStartupCheck(new StartupCheck(ContextsWiredCheck, StartupCheckStage.Services, static async (provider, _) =>
        {
            var scope = provider.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                foreach (var contextType in EntityFrameworkChecks.RegisteredContexts(scope.ServiceProvider))
                {
                    EnsureWired((DbContext)scope.ServiceProvider.GetRequiredService(contextType));
                }
            }
        }));

        services.AddStartupCheck(new StartupCheck(UnknownStoredKeysCheck, StartupCheckStage.Database, static async (provider, cancellationToken) =>
        {
            var catalogue = provider.GetRequiredService<TenancyCatalogue>();
            var scope = provider.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var tenancy = scope.ServiceProvider.GetRequiredService<TContext>();
                var unknown = await UnknownStoredKeysAsync<TRole, TRoleId, TTenantId>(tenancy, catalogue, cancellationToken).ConfigureAwait(false);
                if (unknown.Count == 0)
                {
                    return;
                }

                var logger = provider.GetService<ILoggerFactory>()?.CreateLogger(typeof(TenancyChecks).FullName!) ?? NullLogger.Instance;
                foreach (var key in unknown)
                {
                    logger.LogWarning(
                        "A role holds the key {Key}, which the catalogue does not know. It gives no rights. Retire a key rather than removing it from the code, or take it off the roles that hold it.",
                        key);
                }
            }
        }));
    }

    private static int IndexOf<TInterceptor>(List<IInterceptor> interceptors)
    {
        for (var index = 0; index < interceptors.Count; index++)
        {
            if (interceptors[index] is TInterceptor)
            {
                return index;
            }
        }

        return -1;
    }
}
