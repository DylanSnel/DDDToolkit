using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Marks the unit at which the key of a request's <see cref="RequiresKeyAttribute"/> is held, there or at a unit
/// above it: what <see cref="TenancyAccess.AtUnit{TUnitId}"/> says, written on the member of the request it reads.
/// <code>
/// [RequiresKey(TenancyKeys.GrantsManage)]
/// public sealed record MakeGrant(SeatId Seat, [AtUnit] OrganizationUnitId Unit, RoleId Role) : ICommand, ITenantsRequest;
/// </code>
/// is <c>TenancyAccess.AtUnit(TenancyKeys.GrantsManage, request.Unit)</c>.
/// </summary>
/// <remarks>
/// It goes on a positional parameter of a record as it is, or on a property, and <c>[property: AtUnit]</c> is read as
/// well. The member is the application's unit id, the id its class declared with <c>[OrganizationUnit&lt;TUnitId&gt;]</c>
/// is declared over: on another id, a seat's say, it is DDD00081, where the project sees that class. A unit that may
/// be left out, <c>OrganizationUnitId?</c>, is a requirement computed from whether it is given, so the request answers
/// <c>RequiredAccess</c> itself: <c>Unit is { } unit ? TenancyAccess.AtUnit(key, unit) : TenancyAccess.ForTheWholeTenant(key)</c>.
/// A key without any marker is held for the whole tenant.
/// </remarks>
[KeyMarker(typeof(TenancyAccess), nameof(TenancyAccess.AtUnit), IdOf = typeof(OrganizationUnitAttribute<>))]
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
public sealed class AtUnitAttribute : Attribute;
