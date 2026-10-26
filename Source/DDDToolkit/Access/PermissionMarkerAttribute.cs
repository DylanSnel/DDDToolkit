namespace DDDToolkit.Access;

/// <summary>
/// Makes an attribute a package ships a marker that says where the permission of <see cref="RequiresPermissionAttribute"/> is held,
/// and says the call that makes the requirement from the permission and the member it marks. Tenancy declares its marker of a
/// unit this way, and Membership its marker of a resource:
/// <code>
/// [PermissionMarker(typeof(TenancyAccess), nameof(TenancyAccess.AtUnit), IdOf = typeof(OrganizationUnitAttribute&lt;&gt;))]
/// [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
/// public sealed class AtUnitAttribute : Attribute;
/// </code>
/// A request that carries <c>[RequiresPermission(permission)]</c> and marks a member <c>[AtUnit]</c> then answers
/// <see cref="IRequireAccess.RequiredAccess"/> with <c>TenancyAccess.AtUnit(permission, request.Unit)</c>.
/// </summary>
/// <remarks>
/// The generator writes the method of <see cref="Factory"/> named <see cref="Method"/>, with the permission and the marked
/// member, and then whatever a <see cref="PermissionMarkerArgumentAttribute"/> beside it marks. A marker goes on a positional
/// parameter of a record or on a property, so declare it for both, <c>AttributeTargets.Parameter |
/// AttributeTargets.Property</c>: a bare marker on a positional parameter is the parameter's, and the generator reads
/// <c>[property: AtUnit]</c> as well. One marker says where for one requirement, so two on one request are DDD00080.
/// </remarks>
/// <param name="factory">The class whose static method makes the requirement: <c>typeof(TenancyAccess)</c>.</param>
/// <param name="method">The method's name: <c>nameof(TenancyAccess.AtUnit)</c>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PermissionMarkerAttribute(Type factory, string method) : Attribute
{
    /// <summary>The class whose static method makes the requirement.</summary>
    public Type Factory { get; } = factory;

    /// <summary>The name of the method that makes it.</summary>
    public string Method { get; } = method;

    /// <summary>
    /// The template whose classes' ids the marked member may be, as <c>[TemplateType]</c> names one: the open generic
    /// attribute an application declares the class with, <c>typeof(OrganizationUnitAttribute&lt;&gt;)</c>. A member of
    /// another type is DDD00081: a unit marked on a seat's id compiles, and lets nobody through. Where the project sees
    /// no class declared with the template, nothing is said, and the package's check stops an id it does not know
    /// when the request is sent. Null to take any type the method takes.
    /// </summary>
    public Type? IdOf { get; set; }

    /// <summary>
    /// Which type argument of <see cref="IdOf"/>, as the class is declared with it, names what the member is the id of:
    /// zero-based, and 0 by default, the class's own id. Membership's marker of a resource takes the fourth, the
    /// resource a member class is declared for, with <see cref="IdOfArgument"/>.
    /// </summary>
    public int Argument { get; set; }

    /// <summary>
    /// With a later <see cref="Argument"/> that names an entity or an aggregate root: the member is the id that class
    /// is declared with, rather than the class itself. <c>[Member&lt;DocumentShareId, UserId, NamedRole, Document&gt;]</c>
    /// with <c>Argument = 3, IdOfArgument = true</c> makes the member a <c>DocumentId</c>.
    /// </summary>
    public bool IdOfArgument { get; set; }

    /// <summary>
    /// The requirement attributes a permission held where this marker says already requires, so that one of them stacked
    /// beside <see cref="RequiresPermissionAttribute"/> says nothing more: a permission at a unit of Tenancy's is held by a seat in the
    /// tenant or by system work there, so <c>[AtUnit]</c> names <c>[InTenant]</c>, and <c>[InTenant]</c> beside it is
    /// DDD00084, a warning. Empty by default.
    /// </summary>
    public Type[] Implies { get; set; } = [];

    /// <summary>
    /// Says that <see cref="OrOwnAttribute"/> may go beside this marker, by naming the method of <see cref="Factory"/>
    /// whose case the caller's own way through still meets where the marker says: the member it marks is what the request
    /// acts on, so a caller who holds the permission there acts on it, and one who acts on its own acts on its own there, and is
    /// held to it there too. The method takes what <see cref="Method"/> takes, the permission, the marked member and the
    /// arguments beside it. Membership's <c>[OnResource]</c> names <c>MemberAccess.Sees</c>:
    /// <c>LeaveCrew([OnResource] ProjectId Project, [OrOwn] SeatId Seat)</c> takes the caller off a crew of a project it
    /// sees, or anyone with the permission on the project, and is
    /// <c>Any(All(Own(request.Seat), MemberAccess.Sees(permission, request.Project)), MemberAccess.On(permission, request.Project))</c>,
    /// so the project's check reads it, compares the version the request names and keeps what a save is held to, either
    /// way. <see langword="null"/> by default, for a marker of where a permission is checked whatever the request acts on, as
    /// Tenancy's <c>[AtUnit]</c> is: beside it the caller would send a unit where it holds the permission for another subject, so
    /// <c>[OrOwn]</c> there is DDD00085.
    /// </summary>
    public string? OwnAlternative { get; set; }
}
