using System.Transactions;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// The reads Tenancy makes across tenants: the few whose answer is needed before any tenant is known, such as
/// the keys stored on every tenant's roles at start-up, or which tenants a round of system work visits. Each is
/// one query that answers ids or keys only, and every such read goes through here, so the list stays short and
/// known.
/// <para>
/// Who a read runs as depends on who keeps the rights. Where the database does
/// (<see cref="TenancyStoreOptions.DatabaseKeepsRights"/>, as on Postgres with
/// <c>DDDToolkit.Supporting.Tenancy.Postgres</c>), it runs as scoped system work, <see cref="Caller.SystemIn"/>,
/// in no tenant: a role the policies hold, which reads no row of any of Tenancy's tables there, and asks a
/// function of the database (<see cref="TenancyFunctionNames"/>) that answers this one question and nothing
/// else. Nothing in Tenancy then runs as the application itself, past the policies. Anywhere else there are no
/// such functions and no policies to hold anyone, and the read runs as the toolkit's <see cref="Caller.System"/>,
/// over the tables.
/// </para>
/// <para>
/// Either way it runs on a context of its own, from a new scope of the application's services, never on the
/// caller's: a transaction or an open connection the caller holds would otherwise decide who the read runs as,
/// narrowing it to the caller's tenant or carrying the read's reach into the caller's work. For the same reason
/// it runs outside an ambient <c>TransactionScope</c> the caller began, rather than enlisted in it, and the
/// caller it begins ends with the query.
/// </para>
/// </summary>
public static class TenancySystemReads
{
    /// <summary>
    /// The ids of the tenants a round of system work visits: the active and the suspended ones, in no order. A
    /// loop of any module asks this first, outside any tenant, and then begins its own system work in each
    /// tenant in turn (<see cref="TenancyWork.BeginSystemIn{TTenantId, TSeatId}(TTenantId, TSeatId?, string)"/>,
    /// in its own scope). The ids are all it learns here: they name no person and no row.
    /// <para>
    /// Where the database keeps the rights, the read runs as <c>Caller.SystemIn(<paramref name="scope"/>)</c> in
    /// no tenant and asks the database's function <see cref="TenancyFunctionNames.TenantsToSweep"/>, which
    /// answers system work of every scope; anywhere else it reads the tenants' table as the application itself.
    /// </para>
    /// </summary>
    /// <typeparam name="TTenant">The application's tenant class.</typeparam>
    /// <typeparam name="TTenantId">The application's tenant id.</typeparam>
    /// <param name="context">
    /// A context of Tenancy's, resolved from the application's services: it names the context type and the
    /// services the read makes its own context from, and is not queried itself.
    /// </param>
    /// <param name="scope">
    /// The scope of the work that asks, the module's name: lower case letters, digits, <c>_</c> and <c>-</c>.
    /// </param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is not a scope; nothing is read then, on any database.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="context"/> is not Tenancy's: its model does not map the tenants; or it was not made by the
    /// application's services.
    /// </exception>
    public static Task<IReadOnlyList<TTenantId>> TenantsToSweepAsync<TTenant, TTenantId>(DbContext context, string scope, CancellationToken cancellationToken)
        where TTenant : TenantAggregate<TTenantId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    {
        ArgumentNullException.ThrowIfNull(context);

        // A module's loop has its own context at hand, and that one maps no tenants: said here, rather than by
        // whichever of the two reads would trip over it.
        if (context.Model.FindEntityType(typeof(TTenant)) is null)
        {
            throw new InvalidOperationException(
                "'" + context.GetType().Name + "' does not map " + typeof(TTenant).Name + ", so it is not the context Tenancy's tables are in, and the tenants to visit are read through that one. "
                + "Pass the context whose model calls modelBuilder.AddTenancy(), resolved from the application's services.");
        }

        return RunAsync<IReadOnlyList<TTenantId>>(
            context,
            scope,
            overTables: async (own, cancellation) => await own.Set<TTenant>()
                .IgnoreQueryFilters()
                .Where(tenant => tenant.Status == TenantStatus.Active || tenant.Status == TenantStatus.Suspended)
                .Select(tenant => tenant.Id)
                .ToListAsync(cancellation)
                .ConfigureAwait(false),
            throughFunctions: async (own, cancellation) =>
            {
                // A row of the model, so the application's converter makes the ids; sent as it is written here.
                var answered = await own.Set<TenantToSweepRow<TTenantId>>()
                    .FromSqlRaw(TenancyFunctionSql.SelectValues(
                        own,
                        schema: null,
                        TenancyFunctionNames.TenantsToSweep,
                        parameters: 0,
                        nameof(TenantToSweepRow<TTenantId>.TenantId)))
                    .ToListAsync(cancellation)
                    .ConfigureAwait(false);
                return [.. answered.Select(row => row.TenantId)];
            },
            cancellationToken);
    }

    /// <summary>
    /// Runs one read across tenants on a new context of the same type as <paramref name="like"/>, made in a new
    /// scope of the services <paramref name="like"/> was made from: <paramref name="throughFunctions"/> as
    /// <c>Caller.SystemIn(<paramref name="scope"/>)</c> with no Tenancy caller, so in no tenant, where the
    /// database keeps the rights; <paramref name="overTables"/> as <see cref="Caller.System"/> anywhere else.
    /// </summary>
    /// <param name="like">A context of the type to read, made by the application's services: it names the type and the services, and is not read.</param>
    /// <param name="scope">The scope the read runs in where the database keeps the rights.</param>
    /// <param name="overTables">The one query over Tenancy's tables, past every filter, on the new context.</param>
    /// <param name="throughFunctions">The one query that asks the database's function, on the new context.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is not a scope.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="like"/> was not made by the application's services, or they have no context registered
    /// under its own type.
    /// </exception>
    internal static async Task<T> RunAsync<T>(
        DbContext like,
        string scope,
        Func<DbContext, CancellationToken, Task<T>> overTables,
        Func<DbContext, CancellationToken, Task<T>> throughFunctions,
        CancellationToken cancellationToken)
    {
        // Made before anything else, so a scope that is none is refused the same on every database.
        var scoped = Caller.SystemIn(scope);

        var services = like.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider
            ?? throw new InvalidOperationException(
                "'" + like.GetType().Name + "' was not made by the application's services, and a read across tenants needs them: it runs on a context "
                + "of its own, never on the caller's. Register the context with AddDbContext<" + like.GetType().Name + ">, or with a context pool and services.AddScopedFromPool<"
                + like.GetType().Name + ">(), and resolve it from a scope.");

        // The new context is of the same type, with the same options, as the one given: this one says which
        // database it is.
        var asksTheDatabase = like.Database.IsRelational()
                              && services.GetService<IOptions<TenancyStoreOptions>>()?.Value.DatabaseKeepsRights == true;

        // The caller is begun before the context is taken: resolving it takes the context, and a host may make
        // one for whoever is calling, so it is taken as the read's own caller too, never as the seat the work
        // around it acts as. Where the database keeps the rights that is scoped system work in no tenant,
        // whatever seat or tenant the work around this read acts in: the tenant would travel to the database
        // next to the scope, and make this the system work of that tenant, which reads and, in Tenancy's own
        // scope, writes it. With none, the tables answer nothing, and only the function does.
        using (new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
        using (Callers.Begin(asksTheDatabase ? scoped : Caller.System))
        using (asksTheDatabase ? TenancyCallers.BeginNone() : null)
        {
            await using var lifetime = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            var context = lifetime.ServiceProvider.GetService(like.GetType()) as DbContext
                ?? throw new InvalidOperationException(
                    "No '" + like.GetType().Name + "' is registered under its own type, and a read across tenants makes one of its own from the application's services. "
                    + "Register the context with AddDbContext<" + like.GetType().Name + ">, or with a context pool and services.AddScopedFromPool<" + like.GetType().Name + ">().");

            return await (asksTheDatabase ? throughFunctions : overTables)(context, cancellationToken).ConfigureAwait(false);
        }
    }
}
