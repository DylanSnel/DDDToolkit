using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// The query filter that keeps every read of a tenant's rows to the current Tenancy caller's tenant. It
/// matches the rows of the tenant a seat or system work in a tenant acts in, and nothing for nobody and for
/// system work outside any tenant.
/// <para>
/// It is a named filter, so an application's own filter on the same entity, a soft delete say, is another
/// named filter and both apply. A read that must look across tenants for the application, as finding a
/// person's seats does, skips it by name with <c>IgnoreQueryFilters([TenancyQueryFilter.Name])</c>, which
/// leaves every other filter in place. Tenancy's own bookkeeping, the rows a save writes and the checks that
/// stand in for a unique index, skips every filter: it must see each row the database holds.
/// </para>
/// </summary>
public static class TenancyQueryFilter
{
    /// <summary>The filter's name, for <c>IgnoreQueryFilters</c>.</summary>
    public const string Name = "tenancy.caller-tenant";

    private static readonly MethodInfo CurrentMethod = typeof(TenancyQueryFilter).GetMethod(nameof(Current))!;

    /// <summary>
    /// The tenant the current Tenancy caller acts in, or <see langword="null"/> when it acts in none, which
    /// the filter matches with nothing. The filter calls this; code that wants the tenant asks
    /// <c>ITenancyAnswers.RequireTenant()</c>.
    /// <para>
    /// The parameter is what makes the filter follow the caller. Entity Framework caches a query with the
    /// filter's expression in it, and it would evaluate a call that takes nothing once, when the query is
    /// compiled, and keep that tenant for every later execution. A call that takes the context is evaluated
    /// again for every execution, with the context that runs it; the filter passes a constant of type
    /// <see cref="DbContext"/> for Entity Framework to replace.
    /// </para>
    /// </summary>
    /// <typeparam name="TTenantId">The application's tenant id.</typeparam>
    /// <param name="context">The context running the query; not read.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TTenantId? Current<TTenantId>(DbContext? context)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        => TenancyCallers.CurrentTenantOrNull() as TTenantId?;

    /// <summary>
    /// The filter for an entity whose tenant <paramref name="tenant"/> reads:
    /// <c>entity =&gt; (TTenantId?)entity.TenantId == Current&lt;TTenantId&gt;(context)</c>. Built from nodes
    /// because the ids are generic here, and the equality of an id is only known once they are closed.
    /// </summary>
    internal static Expression<Func<TEntity, bool>> Of<TEntity, TTenantId>(Expression<Func<TEntity, TTenantId>> tenant)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    {
        var current = Expression.Call(
            CurrentMethod.MakeGenericMethod(typeof(TTenantId)),
            Expression.Constant(null, typeof(DbContext)));

        var body = Expression.Equal(Expression.Convert(tenant.Body, typeof(TTenantId?)), current);
        return Expression.Lambda<Func<TEntity, bool>>(body, tenant.Parameters);
    }
}
