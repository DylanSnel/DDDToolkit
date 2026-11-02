using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// At a unit. On a member of a request, with a permission, it marks the unit at which the caller holds that permission,
/// there or at a unit above it. On a constant of a <see cref="PermissionsAttribute"/> class, without a permission, it
/// declares that the permission is asked at a unit. Both uses mean the same: the permission is held at a unit.
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
/// On the request, the generator turns it into a call of <see cref="TenancyAccess.AtUnit{TUnitId}"/> on the marked member:
/// <c>TenancyAccess.AtUnit(BoardPermissions.Create, request.Unit)</c>.
/// </summary>
/// <remarks>
/// On a request it goes on a positional parameter of a record, or on a property. The generator also reads
/// <c>[property: AtUnit(permission)]</c>. The member is the application's unit id: the id that the class declared with
/// <c>[OrganizationUnit&lt;TUnitId&gt;]</c> is declared over. On another id, such as a seat's id, it is DDD00081, in a
/// project that sees that class. A unit that may be left out, <c>OrganizationUnitId?</c>, makes a requirement that
/// depends on whether the unit is given. Such a request implements <c>RequiredAccess</c> itself:
/// <c>Unit is { } unit ? TenancyAccess.AtUnit(permission, unit) : TenancyAccess.AtTenant(permission)</c>. On a request's
/// member, <c>[AtUnit]</c> without a permission is DDD00080. Two unit markers on one request are both required, and the
/// generator combines them with <c>AccessRequirement.All</c>. A request that moves a seat from one unit to another needs
/// this.
/// <para>
/// On a permission constant it is the permission's level. Tenancy's generator reads it into the catalogue under the
/// module, with the settings of <see cref="PermissionLevelAttribute"/>. These settings have no effect on a request's
/// member. <c>[AtUnit(permission)]</c> on a constant is DDD00092, because the constant is the permission. A permission
/// asked at a unit is held there by a role of the organization granted at that unit or above it. The grant reaches every
/// unit below.
/// </para>
/// <para>
/// <c>[OrOwn]</c> does not pair with it. A caller could send the unit where the permission is checked together with
/// another person's seat. A permission held at one unit would then act on a seat anywhere (DDD00085). Only a seat in the
/// tenant, or system work there, holds a permission at a unit. So <c>[InTenant]</c> beside the marker adds nothing, and
/// is a warning (DDD00084).
/// </para>
/// </remarks>
[PermissionMarker(typeof(TenancyAccess), nameof(TenancyAccess.AtUnit), IdOf = typeof(OrganizationUnitAttribute<>), Implies = [typeof(InTenantAttribute)])]
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
public sealed class AtUnitAttribute : PermissionLevelAttribute
{
    /// <summary>Declares the level of a permission constant: the permission is asked at a unit.</summary>
    public AtUnitAttribute()
    {
    }

    /// <summary>Marks the unit of a request at which the caller holds <paramref name="permission"/>.</summary>
    /// <param name="permission">The permission the caller holds at the unit: a constant, one of the catalogue's.</param>
    public AtUnitAttribute(string permission) => Permission = permission;

    /// <summary>The permission that the caller holds at the unit, on a request's member. Null on a permission constant.</summary>
    public string? Permission { get; }
}
