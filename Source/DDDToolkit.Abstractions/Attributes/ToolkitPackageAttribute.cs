namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Marks an assembly as one of the toolkit's packages whose classes an application can return from its API, such as
/// Tenancy and Membership. The public methods of the classes in such an assembly are behaviour, and an API layer does
/// not publish them.
/// <code>
/// [assembly: ToolkitPackage]
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// Entities, aggregates and value objects are known by their interfaces. A package also has plain classes, such as
/// Membership's <c>MemberRole&lt;TMemberId, TRoleId&gt;</c> and Tenancy's <c>RoleGrant&lt;TSeatId, TRoleId&gt;</c>. They
/// carry methods such as <c>AppliesAt(moment)</c>. <c>DDDToolkit.HotChocolate</c> leaves the methods of these classes out
/// of a type that binds its fields by convention, as it does for an entity's methods.
/// </para>
/// <para>
/// An application does not put the mark on its own assemblies.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ToolkitPackageAttribute : Attribute;
