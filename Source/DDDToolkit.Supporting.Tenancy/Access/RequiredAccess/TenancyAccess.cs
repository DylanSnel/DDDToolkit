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
/// AccessRequirement IRequireAccess.RequiredAccess =&gt; TenancyAccess.RequiresOperator();
/// </code>
/// Each answers one of <see cref="TenancyRequirement"/>'s cases, which the check that
/// <c>services.AddTenancyAccess&lt;TRequests, TContext&gt;()</c> adds holds the caller to. Where the requirement reads
/// no value of the request but the permission and a unit it carries, the request says it with an attribute instead, and the
/// toolkit's generator writes the same call: <see cref="InTenantAttribute"/>, <c>[RequiresPermission(permission)]</c> for the
/// whole tenant, with <see cref="AtUnitAttribute"/> on the unit for a permission there, and
/// <see cref="RequiresOperatorAttribute"/>. These methods stay for a requirement computed from the request's
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
    /// system work (<see cref="TenancyRequirement.Operator"/>). For queries only.
    /// </summary>
    /// <remarks>
    /// Named for the one kind of caller it lets through. A query says it with <see cref="RequiresOperatorAttribute"/>
    /// as well.
    /// </remarks>
    public static TenancyRequirement.Operator RequiresOperator() => new();

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
}
