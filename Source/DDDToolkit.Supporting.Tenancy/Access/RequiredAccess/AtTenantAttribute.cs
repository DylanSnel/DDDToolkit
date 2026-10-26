using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// The level of a permission that is asked for the whole tenant: held by a role of the organization granted at its
/// root, the way <see cref="TenancyAccess.AtTenant"/> asks it, and what a request's <see cref="RequiresKeyAttribute"/>
/// without a marker requires. A constant of a <see cref="PermissionsAttribute"/> class carries it.
/// <code>
/// [Permissions]
/// public static class BoardPermissions
/// {
///     /// &lt;summary&gt;Make and change the roles of boards.&lt;/summary&gt;
///     [AtTenant]
///     public const string ManageRoles = "boards.roles.manage";
/// }
/// </code>
/// </summary>
/// <remarks>
/// Tenancy's generator puts it into the catalogue, under the module the project declares, with what
/// <see cref="PermissionLevelAttribute"/> says of it, so an organization's role can hold it. With <c>[AtUnit]</c> beside
/// it, a key asked both ways, for the whole tenant and at a unit, says so.
/// </remarks>
[AttributeUsage(AttributeTargets.Field, Inherited = false)]
public sealed class AtTenantAttribute : PermissionLevelAttribute;
