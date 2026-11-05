namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Gives a class the name that every API layer uses for its type. The name has no spaces. A package puts it on a
/// generic class that an application closes once, so the type is not named after its type arguments.
/// <code>
/// [DisplayName("Placement")]
/// public sealed class Placement&lt;TSeatId, TUnitId, TRoleId&gt;
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <c>DDDToolkit.HotChocolate</c> uses the name for the object type and the interface type over the class. Without
/// it, HotChocolate names <c>Placement&lt;SeatId, OrganizationUnitId, RoleId&gt;</c>
/// <c>PlacementOfSeatIdAndOrganizationUnitIdAndRoleId</c>. A name the application gives the type itself, with
/// <c>descriptor.Name(...)</c>, takes precedence. A name that is not a GraphQL name, with letters, digits and underscores
/// only and no digit first, fails the schema build with a message that names the class.
/// </para>
/// <para>
/// A generic class that an application can close more than once gets no display name. Membership's
/// <c>MemberRole&lt;TMemberId, TRoleId&gt;</c> is one: an application with boards and projects closes it twice, and two
/// types of one name would be one type in a composed schema.
/// </para>
/// <para>
/// <c>System.ComponentModel.DisplayNameAttribute</c> has the same simple name. A file that imports both namespaces
/// needs an alias for one of them.
/// </para>
/// </remarks>
/// <param name="name">The type's name: no spaces, and in GraphQL letters, digits and underscores, with no digit first.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DisplayNameAttribute(string name) : Attribute
{
    /// <summary>The type's name.</summary>
    public string Name { get; } = string.IsNullOrWhiteSpace(name)
        ? throw new ArgumentException("A display name needs at least one character.", nameof(name))
        : name;
}
