namespace DDDToolkit.Access;

/// <summary>
/// Makes an attribute a package ships a marker of where a permission is held. The marker goes on the member of a request
/// that names the place, carries the permission, and stands for the call that makes the requirement from the two.
/// Membership declares its marker of a resource this way, and Tenancy its marker of a unit:
/// <code>
/// [PermissionMarker(typeof(MemberAccess), nameof(MemberAccess.On), IdOf = typeof(MemberAttribute&lt;,,,&gt;), Argument = 3, IdOfArgument = true)]
/// [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
/// public sealed class OnResourceAttribute(string permission) : Attribute
/// {
///     public string Permission { get; } = permission;
/// }
/// </code>
/// A request that marks a member <c>[OnResource(BoardPermissions.Edit)] BoardId Board</c> then answers
/// <see cref="IRequireAccess.RequiredAccess"/> with <c>MemberAccess.On(BoardPermissions.Edit, request.Board)</c>.
/// </summary>
/// <remarks>
/// The marker's constructor takes the permission, a string. The generator reads that argument as the permission. It
/// writes the method of <see cref="Factory"/> named <see cref="Method"/> with the permission and the marked member, and
/// then whatever a <see cref="PermissionMarkerArgumentAttribute"/> beside it marks. A marker applied without a
/// permission names none, and on a request's member that is DDD00080. A marker goes on a positional parameter of a
/// record or on a property, so declare it for both, <c>AttributeTargets.Parameter | AttributeTargets.Property</c>. A
/// marker on a positional parameter is the parameter's, and the generator reads <c>[property: OnResource(permission)]</c>
/// as well.
/// <para>
/// Each marker is one requirement. Several markers on one request all hold. The generator writes
/// <c>AccessRequirement.All</c> of their calls, in the order they are written, after the requirement attributes on the
/// request, as it does for stacked attributes. Two markers of one kind are fine, such as two boards or two units.
/// <c>AttachAsset([OnResource(BoardPermissions.Edit)] BoardId Board, [OnResource(AssetPermissions.Use)] AssetId Asset)</c> is
/// <c>AccessRequirement.All(MemberAccess.On(BoardPermissions.Edit, request.Board), MemberAccess.On(AssetPermissions.Use, request.Asset))</c>.
/// </para>
/// </remarks>
/// <param name="factory">The class whose static method makes the requirement: <c>typeof(MemberAccess)</c>.</param>
/// <param name="method">The method's name: <c>nameof(MemberAccess.On)</c>.</param>
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
    /// another type is DDD00081. A unit marked on a seat's id compiles, and lets nobody through. Where the project sees
    /// no class declared with the template, nothing is said, and the package's check stops an id it does not know when
    /// the request is sent. Null to take any type the method takes.
    /// </summary>
    public Type? IdOf { get; set; }

    /// <summary>
    /// Which type argument of <see cref="IdOf"/>, as the class is declared with it, names what the member is the id of.
    /// It is zero-based, and 0 by default, the class's own id. Membership's marker of a resource takes the fourth, the
    /// resource a member class is declared for, with <see cref="IdOfArgument"/>.
    /// </summary>
    public int Argument { get; set; }

    /// <summary>
    /// With a later <see cref="Argument"/> that names an entity or an aggregate root, the member is the id that class
    /// is declared with, and not the class itself. <c>[Member&lt;DocumentShareId, UserId, NamedRole, Document&gt;]</c>
    /// with <c>Argument = 3, IdOfArgument = true</c> makes the member a <c>DocumentId</c>.
    /// </summary>
    public bool IdOfArgument { get; set; }

    /// <summary>
    /// The requirement attributes a permission held where this marker says already requires, so that one of them stacked
    /// beside the marker says nothing more. A permission at a unit of Tenancy's is held by a seat in the tenant or by system
    /// work there, so <c>[AtUnit]</c> names <c>[InTenant]</c>, and <c>[InTenant]</c> beside it is DDD00084, a warning.
    /// Empty by default.
    /// </summary>
    public Type[] Implies { get; set; } = [];

    /// <summary>
    /// Says that <see cref="OrOwnAttribute"/> may pair with this marker, by naming the method of <see cref="Factory"/>
    /// whose case the caller's own way through still meets where the marker says. The member the marker marks is what the
    /// request acts on. A caller who holds the permission there acts on it, and a caller who acts on its own acts on its
    /// own there and is held to it there too. The method takes what <see cref="Method"/> takes: the permission, the marked
    /// member and the arguments beside it. Membership's <c>[OnResource]</c> names <c>MemberAccess.Sees</c>, so
    /// <c>LeaveCrew([OnResource(ProjectPermissionNames.ManageCrew)] ProjectId Project, [OrOwn] SeatId Seat)</c> is
    /// <c>Any(All(Own(request.Seat), MemberAccess.Sees(permission, request.Project)), MemberAccess.On(permission, request.Project))</c>.
    /// The project's check reads the project, compares the version the request names and keeps what a save is held to,
    /// either way. <see langword="null"/> by default, for a marker of where a permission is checked whatever the request
    /// acts on, as Tenancy's <c>[AtUnit]</c> is. Beside such a marker the caller would send a unit where it holds the
    /// permission together with another subject, so <c>[OrOwn]</c> paired with it is DDD00085.
    /// </summary>
    public string? OwnAlternative { get; set; }
}
