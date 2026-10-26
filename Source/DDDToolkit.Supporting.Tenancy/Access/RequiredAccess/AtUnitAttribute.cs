using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// At a unit: on a member of a request, the unit at which the permission of its <see cref="RequiresPermissionAttribute"/> is held,
/// there or at a unit above it; on a constant of a <see cref="PermissionsAttribute"/> class, that the permission is
/// asked at a unit. The one meaning, said where the permission is required and where it is declared.
/// <code>
/// [Permissions]
/// public static class BoardPermissions
/// {
///     /// &lt;summary&gt;Open a board at a unit.&lt;/summary&gt;
///     [AtUnit]
///     public const string Create = "boards.create";
/// }
/// </code>
/// On a request it is what <see cref="TenancyAccess.AtUnit{TUnitId}"/> says, written on the member of the request it
/// reads.
/// <code>
/// [RequiresPermission(TenancyPermissions.GrantsManage)]
/// public sealed record MakeGrant(SeatId Seat, [AtUnit] OrganizationUnitId Unit, RoleId Role) : ICommand, ITenantsRequest;
/// </code>
/// is <c>TenancyAccess.AtUnit(TenancyPermissions.GrantsManage, request.Unit)</c>.
/// </summary>
/// <remarks>
/// It goes on a positional parameter of a record as it is, or on a property, and <c>[property: AtUnit]</c> is read as
/// well. The member is the application's unit id, the id its class declared with <c>[OrganizationUnit&lt;TUnitId&gt;]</c>
/// is declared over: on another id, a seat's say, it is DDD00081, where the project sees that class. A unit that may
/// be left out, <c>OrganizationUnitId?</c>, is a requirement computed from whether it is given, so the request answers
/// <c>RequiredAccess</c> itself: <c>Unit is { } unit ? TenancyAccess.AtUnit(permission, unit) : TenancyAccess.AtTenant(permission)</c>.
/// A permission without any marker is held at the tenant.
/// <para>
/// On a permission it is its level, read by Tenancy's generator into the catalogue under the module, with what
/// <see cref="PermissionLevelAttribute"/> says of it; the settings of a level say nothing on a request's member. A permission
/// asked at a unit is held there by a role of the organization granted at that unit or above it, and reaches every unit
/// below.
/// </para>
/// <para>
/// Not beside <c>[OrOwn]</c>: the caller would send the unit the permission is checked at for another subject, so a permission held at
/// one unit would act on a seat anywhere (DDD00085). A permission at a unit is held by a seat in the tenant or by system work
/// there, so <c>[InTenant]</c> beside it says nothing more, and is a warning (DDD00084).
/// </para>
/// </remarks>
[PermissionMarker(typeof(TenancyAccess), nameof(TenancyAccess.AtUnit), IdOf = typeof(OrganizationUnitAttribute<>), Implies = [typeof(InTenantAttribute)])]
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
public sealed class AtUnitAttribute : PermissionLevelAttribute;
