namespace DDDToolkit.Access;

/// <summary>
/// Marks a static class whose constants are permissions: every <c>public const string</c> of it is one, the key a
/// request requires and a role gives, declared once, with where it is asked and what holds it.
/// <code>
/// [Permissions]
/// public static class BoardPermissions
/// {
///     /// &lt;summary&gt;See a board and who is on it.&lt;/summary&gt;
///     [On&lt;Board&gt;(EveryMember = true)]
///     public const string View = "boards.view";
///
///     /// &lt;summary&gt;Open a board at a unit.&lt;/summary&gt;
///     [AtUnit]
///     public const string Create = "boards.create";
/// }
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// Each permission carries a level, an attribute derived from <see cref="PermissionLevelAttribute"/>, which says where
/// it is asked: Tenancy's <c>[AtTenant]</c> for the whole tenant and <c>[AtUnit]</c> at a unit of the organization, and
/// Membership's <c>[On&lt;TResource&gt;]</c> on one resource, through its members. A permission without one, or a level
/// on anything that is no such constant, is DDD00092. The constants stay constants, so a request names one where it
/// requires it: <c>[RequiresKey(BoardPermissions.Create)]</c>.
/// </para>
/// <para>
/// The packages' generators collect what is declared here, so the module says it nowhere else: Membership's into the
/// rules of each resource (<c>MembershipRules.For&lt;Board&gt;</c>), and Tenancy's into its catalogue, under the module
/// the project declares with <c>DDD_Module</c>, with the description of each permission:
/// <see cref="PermissionLevelAttribute.Description"/>, or else the constant's <c>&lt;summary&gt;</c>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PermissionsAttribute : Attribute;
