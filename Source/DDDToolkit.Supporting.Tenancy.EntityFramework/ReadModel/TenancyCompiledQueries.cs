using System.Runtime.CompilerServices;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// The access checks' queries over the read model, compiled once per model with <c>EF.CompileAsyncQuery</c>. A check of
/// a permission sends its values as parameters, and Entity Framework neither builds the query again nor looks it up.
/// <para>
/// Each query is the one <c>TenancyQueries</c> composes for the same question over <see cref="ITenancyReadSource{TTenantId, TSeatId, TUnitId, TRoleId}"/>,
/// written over the context's sets, untracked, as <see cref="EfTenancyReadSource{TTenantId, TSeatId, TUnitId, TRoleId}"/>
/// reads them. The tests compare the two answers.
/// </para>
/// </summary>
/// <remarks>
/// A compiled query keeps the model it was compiled for, and compiles again for a context of another model. The queries are
/// kept per model, so a check over Tenancy's own context and a check over a module's context that maps the read model do not
/// compile each other's away. The query filters apply as to any other query of the context.
/// </remarks>
internal sealed class TenancyCompiledQueries<TTenantId, TSeatId, TUnitId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    private static readonly ConditionalWeakTable<IModel, TenancyCompiledQueries<TTenantId, TSeatId, TUnitId, TRoleId>> PerModel = new();

    private TenancyCompiledQueries()
    {
    }

    /// <summary>
    /// Whether a seat holds a permission at the root unit of its tenant, now: a right granted at the root, which applies at
    /// the moment.
    /// </summary>
    public Func<DbContext, TTenantId, TSeatId, string, DateTimeOffset, CancellationToken, Task<bool>> HoldsAtTenant { get; } = EF.CompileAsyncQuery(
        (DbContext context, TTenantId tenant, TSeatId seat, string permission, DateTimeOffset moment, CancellationToken cancellationToken) =>
            (from right in context.Set<SeatRight<TTenantId, TSeatId, TUnitId, TRoleId>>().AsNoTracking()
             where right.TenantId.Equals(tenant) && right.SeatId.Equals(seat) && right.Permission == permission
                   && right.StartsAt <= moment && (right.EndsAt == null || right.EndsAt > moment)
             join root in context.Set<OrganizationUnitRow<TTenantId, TUnitId>>().AsNoTracking() on right.UnitId equals root.Id
             where root.TenantId.Equals(tenant) && !root.ParentId.HasValue
             select right.Permission).Any());

    /// <summary>
    /// Whether a seat holds a permission at a unit, now: a right granted there or at a unit above it, which applies at the
    /// moment.
    /// </summary>
    public Func<DbContext, TTenantId, TSeatId, string, TUnitId, DateTimeOffset, CancellationToken, Task<bool>> HoldsAt { get; } = EF.CompileAsyncQuery(
        (DbContext context, TTenantId tenant, TSeatId seat, string permission, TUnitId unit, DateTimeOffset moment, CancellationToken cancellationToken) =>
            (from right in context.Set<SeatRight<TTenantId, TSeatId, TUnitId, TRoleId>>().AsNoTracking()
             where right.TenantId.Equals(tenant) && right.SeatId.Equals(seat) && right.Permission == permission
                   && right.StartsAt <= moment && (right.EndsAt == null || right.EndsAt > moment)
             join path in context.Set<OrganizationUnitPath<TTenantId, TUnitId>>().AsNoTracking() on right.UnitId equals path.AncestorId
             where path.TenantId.Equals(tenant)
             select path.DescendantId).Distinct().Where(held => held.Equals(unit)).Any());

    /// <summary>Whether a unit is one of a tenant's.</summary>
    public Func<DbContext, TTenantId, TUnitId, CancellationToken, Task<bool>> HasUnit { get; } = EF.CompileAsyncQuery(
        (DbContext context, TTenantId tenant, TUnitId unit, CancellationToken cancellationToken) =>
            context.Set<OrganizationUnitRow<TTenantId, TUnitId>>().AsNoTracking().Where(row => row.TenantId.Equals(tenant) && row.Id.Equals(unit)).Any());

    /// <summary>The queries for <paramref name="model"/>, compiled the first time a context of that model asks.</summary>
    public static TenancyCompiledQueries<TTenantId, TSeatId, TUnitId, TRoleId> For(IModel model)
        => PerModel.GetValue(model, static _ => new TenancyCompiledQueries<TTenantId, TSeatId, TUnitId, TRoleId>());
}
