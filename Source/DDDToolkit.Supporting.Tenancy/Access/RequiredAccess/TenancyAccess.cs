using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// How a request spells what it requires where Tenancy is the one that knows, beside the core's own
/// (<c>AccessRequirement.SignedIn()</c> and the like) and Membership's (<c>MemberAccess.On(permission, resource)</c>):
/// <code>
/// AccessRequirement IRequireAccess.RequiredAccess =&gt; TenancyAccess.InTenant();
/// AccessRequirement IRequireAccess.RequiredAccess =&gt; TenancyAccess.AtTenant(TenancyPermissions.RolesManage);
/// AccessRequirement IRequireAccess.RequiredAccess =&gt; TenancyAccess.AtUnit(TenancyPermissions.GrantsManage, Unit);
/// AccessRequirement IRequireAccess.RequiredAccess =&gt; TenancyAccess.Operator();
/// </code>
/// Each answers one of <see cref="TenancyRequirement"/>'s cases, which the check that
/// <c>services.AddTenancyAccess&lt;TRequests, TContext&gt;()</c> adds holds the caller to. Where the requirement reads
/// no value of the request but the permission and a unit it carries, the request says it with an attribute instead, and the
/// toolkit's generator writes the same call: <see cref="InTenantAttribute"/>, <c>[RequiresPermission(permission)]</c> for the
/// whole tenant, with <see cref="AtUnitAttribute"/> on the unit for a permission there, and
/// <see cref="OperatorAttribute"/>, with <see cref="ForTenantAttribute"/> on the tenant. These methods stay for a requirement computed from the request's
/// values: <c>Unit is { } unit ? TenancyAccess.AtUnit(permission, unit) : TenancyAccess.AtTenant(permission)</c>.
/// </summary>
/// <remarks>
/// They are the one spelling: the cases' constructors are internal, as the core's are, and these methods make
/// them. Methods rather than constructors, so a case closed over the application's unit id takes it from the
/// argument: <c>AtUnit(permission, unit)</c>.
/// </remarks>
public static class TenancyAccess
{
    /// <summary>
    /// The caller works in the tenant the request was sent for: a seat there, or system work there
    /// (<see cref="TenancyRequirement.InTenant"/>).
    /// </summary>
    public static TenancyRequirement.InTenant InTenant() => new();

    /// <summary>
    /// The caller holds <paramref name="permission"/> for the whole tenant, at its root, now
    /// (<see cref="TenancyRequirement.AtTenant"/>).
    /// </summary>
    /// <param name="permission">The permission the request needs for the whole tenant: one of the catalogue's.</param>
    /// <exception cref="ArgumentException"><paramref name="permission"/> is blank.</exception>
    public static TenancyRequirement.AtTenant AtTenant(string permission) => new(permission);

    /// <summary>
    /// The caller holds <paramref name="permission"/> at <paramref name="unit"/>, there or at a unit above it, now
    /// (<see cref="TenancyRequirement.AtUnit{TUnitId}"/>).
    /// </summary>
    /// <remarks>
    /// The type is taken from the argument, so the compiler does not tell a unit from another id: on a request
    /// that also carries a seat, <c>AtUnit(permission, Seat)</c> compiles. It lets nobody through, since the check
    /// decides the case for the application's unit id alone and stops every send of another with an
    /// <see cref="InvalidOperationException"/> that names it. Where a request carries more ids than one, write
    /// the type, <c>AtUnit&lt;OrganizationUnitId&gt;(permission, Unit)</c>, and the compiler catches the wrong one; a test
    /// that holds every request to the requirement it should declare catches it as well.
    /// </remarks>
    /// <param name="permission">The permission the request needs at the unit: one of the catalogue's.</param>
    /// <param name="unit">The unit, from the request.</param>
    /// <typeparam name="TUnitId">The application's unit id.</typeparam>
    /// <exception cref="ArgumentException"><paramref name="permission"/> is blank.</exception>
    public static TenancyRequirement.AtUnit<TUnitId> AtUnit<TUnitId>(string permission, TUnitId unit)
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        => new(permission, unit);

    /// <summary>
    /// The caller is one of the application's operators, and nobody else is: no seat, whatever it holds, and no
    /// system work (<see cref="TenancyRequirement.Operator"/>). On a command, the handler runs as operator work recorded as
    /// the operator, in no tenant, or with <see cref="OperatorTenant.FromRequest"/> in the tenant the request names. On a
    /// query nothing begins.
    /// </summary>
    /// <remarks>
    /// Named for the one kind of caller it lets through. A request says it with <see cref="OperatorAttribute"/> as well.
    /// </remarks>
    /// <param name="tenant">Where a command's operator work acts: no tenant, the default, or the tenant the request names.</param>
    /// <param name="scope">The scope a command's operator work runs in: lower case letters, digits, <c>_</c> and <c>-</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is not a scope.</exception>
    public static TenancyRequirement.Operator Operator(OperatorTenant tenant = OperatorTenant.FromCommand, string scope = TenancyWork.SystemScope)
        => new(Defined(tenant), ScopeOf(scope));

    /// <summary>
    /// The caller is one of the application's operators, and the request names <paramref name="forTenant"/>
    /// (<see cref="TenancyRequirement.OperatorIn{TTenantId}"/>). On a command, the handler runs as operator work recorded as
    /// the operator, in that tenant. A request that names another tenant beside it is refused with
    /// <c>tenancy.tenant-mismatch</c>. On a query nothing begins.
    /// </summary>
    /// <remarks>
    /// A request says it with <see cref="OperatorAttribute"/> and <see cref="ForTenantAttribute"/> on the member as well.
    /// </remarks>
    /// <param name="forTenant">The tenant the request names, from the request.</param>
    /// <param name="tenant">
    /// With <see cref="OperatorTenant.FromCommand"/>, the request names its tenant. <see cref="OperatorTenant.FromRequest"/>
    /// is for a request that names none, and is refused here.
    /// </param>
    /// <param name="scope">The scope a command's operator work runs in: lower case letters, digits, <c>_</c> and <c>-</c>.</param>
    /// <typeparam name="TTenantId">The application's tenant id.</typeparam>
    /// <exception cref="ArgumentException">
    /// <paramref name="tenant"/> is <see cref="OperatorTenant.FromRequest"/>, which would name a second tenant, or
    /// <paramref name="scope"/> is not a scope.
    /// </exception>
    public static TenancyRequirement.OperatorIn<TTenantId> Operator<TTenantId>(
        TTenantId forTenant,
        OperatorTenant tenant = OperatorTenant.FromCommand,
        string scope = TenancyWork.SystemScope)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        => Defined(tenant) == OperatorTenant.FromRequest
            ? throw new ArgumentException(
                "A request that names its tenant acts in that tenant; OperatorTenant.FromRequest is for a request that names none (DDD00103).",
                nameof(tenant))
            : new(forTenant, ScopeOf(scope));

    /// <summary>
    /// The request is about the caller's own seat: <paramref name="seat"/> is the caller's seat in the tenant of the
    /// request. Anyone else is refused with <c>access.not-own</c>: another seat, a caller with no seat there, an operator,
    /// and system work, which holds every permission in its tenant but has no seat of its own. Spelled beside the other cases,
    /// for a requirement that combines them:
    /// <code>
    /// AccessRequirement IRequireAccess.RequiredAccess =&gt; AccessRequirement.Any(
    ///     TenancyAccess.Own(Seat),
    ///     TenancyAccess.AtTenant(TenancyPermissions.SeatsManage));
    /// </code>
    /// which a request says with attributes as well: <c>[RequiresPermission(TenancyPermissions.SeatsManage)]</c> and
    /// <c>[OrOwn] SeatId Seat</c>.
    /// </summary>
    /// <remarks>
    /// The case is the core's, <see cref="AccessRequirement.Own{TValue}"/>, decided by the core's check of who is calling:
    /// what the caller's own seat is, Tenancy says with the handle <c>AddTenancy</c> registers, from the caller's verified
    /// identity and the tenant the request names. So it reads nothing, and it is the same requirement as
    /// <c>AccessRequirement.Own(seat)</c>.
    /// </remarks>
    /// <param name="seat">The seat the request names.</param>
    /// <typeparam name="TSeatId">The application's seat id.</typeparam>
    public static AccessRequirement.Owned<TSeatId> Own<TSeatId>(TSeatId seat)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        => AccessRequirement.Own(seat);

    /// <summary><paramref name="tenant"/>, where it is one of the values the enum declares.</summary>
    private static OperatorTenant Defined(OperatorTenant tenant)
        => tenant is OperatorTenant.FromCommand or OperatorTenant.FromRequest
            ? tenant
            : throw new ArgumentOutOfRangeException(nameof(tenant), tenant, "Operator work acts in the tenant the command names or in the one the request names.");

    /// <summary><paramref name="scope"/>, where it is a scope as the toolkit's scoped system work takes one.</summary>
    private static string ScopeOf(string scope)
    {
        // The rule is the scoped system work's own, which the work is begun with: asked of it, so the two cannot differ.
        _ = CurrentUserSnapshot.SystemIn(scope);
        return scope;
    }
}
