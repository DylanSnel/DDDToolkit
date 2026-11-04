using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// The query filter that keeps every read of a tenant's rows to the current Tenancy user's tenant. It
/// matches the rows of the tenant a seat or system work in a tenant acts in, and nothing for nobody and for
/// system work outside any tenant.
/// <para>
/// It is a named filter, so an application's own filter on the same entity, a soft delete say, is another
/// named filter and both apply. A read that must look across tenants for the application, as finding a
/// person's seats does, skips it with <see cref="IgnoreTenantFilter{TEntity}"/>, which leaves every other
/// filter in place. Tenancy's own bookkeeping, the rows a save writes and the checks that stand in for a
/// unique index, skips every filter: it must see each row the database holds.
/// </para>
/// </summary>
public static class TenancyQueryFilter
{
    /// <summary>The filter's name, for <c>IgnoreQueryFilters</c>.</summary>
    public const string Name = "tenancy.current-tenant";

    /// <summary>
    /// The filter's name, in the one array every read that skips the filter passes. Entity Framework keeps the
    /// keys of <c>IgnoreQueryFilters</c> as a constant in the query, and its query cache compares that
    /// constant by reference unless it is an array. A new collection on every call, as
    /// <c>IgnoreQueryFilters([Name])</c> writes, made 10.0.0 to 10.0.2 compile the query again on every call.
    /// From 10.0.3 on, Entity Framework copies any collection that is not an array into a new array.
    /// </summary>
    private static readonly string[] Names = [Name];

    private static readonly MethodInfo CurrentMethod = typeof(TenancyQueryFilter).GetMethod(nameof(Current))!;

    /// <summary>
    /// <paramref name="query"/> without the tenant filter, and with every other named filter, a soft delete
    /// say: for a read that must look across tenants for the application, as finding a person's seats does.
    /// <para>
    /// It passes one array for every call. So Entity Framework finds the compiled query in its cache, from
    /// 10.0.0 on, and allocates no copy of the keys.
    /// </para>
    /// </summary>
    /// <typeparam name="TEntity">The entity the query reads.</typeparam>
    /// <param name="query">The query to read past the tenant filter.</param>
    /// <returns>The query, without the tenant filter.</returns>
    public static IQueryable<TEntity> IgnoreTenantFilter<TEntity>(this IQueryable<TEntity> query)
        where TEntity : class
        => query.IgnoreQueryFilters(Names);

    /// <summary>
    /// The tenant the current Tenancy user acts in, or <see langword="null"/> when it acts in none, which
    /// the filter matches with nothing. The filter calls this; code that wants the tenant asks
    /// <c>access.CurrentTenant.Require()</c>.
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
        => TenancyUsers.CurrentTenantOrNull() as TTenantId?;

    /// <summary>
    /// The filter for an entity whose tenant <paramref name="tenant"/> reads:
    /// <c>entity =&gt; (TTenantId?)entity.TenantId == Current&lt;TTenantId&gt;(context)</c>. Built from nodes
    /// because the ids are generic here, and the equality of an id is only known once they are closed.
    /// </summary>
    internal static Expression<Func<TEntity, bool>> Of<TEntity, TTenantId>(Expression<Func<TEntity, TTenantId>> tenant)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        => (Expression<Func<TEntity, bool>>)Of(tenant.Parameters[0], tenant.Body, typeof(TTenantId));

    /// <summary>
    /// The filter for an entity whose tenant is <paramref name="property"/>, a property of the application's tenant id:
    /// what <c>[OfTenant]</c> on the property gives, through <see cref="OfTenantConvention"/>, where no type argument
    /// names the entity or the id.
    /// </summary>
    internal static LambdaExpression Of(Type entity, PropertyInfo property)
    {
        var row = Expression.Parameter(entity, "row");
        return Of(row, Expression.Property(row, property), property.PropertyType);
    }

    private static LambdaExpression Of(ParameterExpression row, Expression tenant, Type tenantId)
    {
        var current = Expression.Call(
            CurrentMethod.MakeGenericMethod(tenantId),
            Expression.Constant(null, typeof(DbContext)));

        var body = Expression.Equal(Expression.Convert(tenant, typeof(Nullable<>).MakeGenericType(tenantId)), current);
        return Expression.Lambda(body, row);
    }
}
