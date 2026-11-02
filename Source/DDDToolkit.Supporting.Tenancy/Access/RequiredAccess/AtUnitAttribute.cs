using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// At a unit. On a member of a request, with a permission, it marks the unit at which the caller holds that permission,
/// there or at a unit above it. On a constant of a <see cref="PermissionsAttribute"/> class, bare, it says that the
/// permission is asked at a unit. The one meaning, said where the permission is required and where it is declared.
/// <code>
/// [Permissions]
/// public static class BoardPermissions
/// {
///     /// &lt;summary&gt;Open a board at a unit.&lt;/summary&gt;
///     [AtUnit]
///     public const string Create = "boards.create";
/// }
///
/// public sealed record OpenBoardCommand([AtUnit(BoardPermissions.Create)] OrganizationUnitId Unit, string Name) : ICommand&lt;Board&gt;, IBoardsRequest;
/// </code>
/// On the request it is what <see cref="TenancyAccess.AtUnit{TUnitId}"/> says, written on the member of the request it
/// reads: <c>TenancyAccess.AtUnit(BoardPermissions.Create, request.Unit)</c>.
/// </summary>
/// <remarks>
/// On a request it goes on a positional parameter of a record as it is, or on a property, and
/// <c>[property: AtUnit(permission)]</c> is read as well. The member is the application's unit id, the id its class
/// declared with <c>[OrganizationUnit&lt;TUnitId&gt;]</c> is declared over. On another id, a seat's say, it is DDD00081,
/// where the project sees that class. A unit that may be left out, <c>OrganizationUnitId?</c>, is a requirement computed
/// from whether it is given, so the request answers <c>RequiredAccess</c> itself:
/// <c>Unit is { } unit ? TenancyAccess.AtUnit(permission, unit) : TenancyAccess.AtTenant(permission)</c>. Without the
/// permission it names none, and on a request's member that is DDD00080. Two units on one request are both required,
/// <c>AccessRequirement.All</c> of the two, as a request that moves a seat from one unit to another needs.
/// <para>
/// On a permission it is its level, read by Tenancy's generator into the catalogue under the module, with what
/// <see cref="PermissionLevelAttribute"/> says of it. The settings of a level say nothing on a request's member.
/// <c>[AtUnit(permission)]</c> on a constant is DDD00092, since the constant is the permission. A permission asked at a unit is held there by a
/// role of the organization granted at that unit or above it, and reaches every unit below.
/// </para>
/// <para>
/// <c>[OrOwn]</c> does not pair with it. The caller would send the unit the permission is checked at together with
/// another subject, so a permission held at one unit would act on a seat anywhere (DDD00085). A permission at a unit is
/// held by a seat in the tenant or by system work there, so <c>[InTenant]</c> beside it says nothing more, and is a
/// warning (DDD00084).
/// </para>
/// </remarks>
[PermissionMarker(typeof(TenancyAccess), nameof(TenancyAccess.AtUnit), IdOf = typeof(OrganizationUnitAttribute<>), Implies = [typeof(InTenantAttribute)])]
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
public sealed class AtUnitAttribute : PermissionLevelAttribute
{
    /// <summary>The level of a permission constant: the permission is asked at a unit.</summary>
    public AtUnitAttribute()
    {
    }

    /// <summary>The marker of a request's unit, at which the caller holds <paramref name="permission"/>.</summary>
    /// <param name="permission">The permission the caller holds at the unit: a constant, one of the catalogue's.</param>
    public AtUnitAttribute(string permission) => Permission = permission;

    /// <summary>The permission the caller holds at the unit, on a request's member; <see langword="null"/> on a permission constant.</summary>
    public string? Permission { get; }
}
