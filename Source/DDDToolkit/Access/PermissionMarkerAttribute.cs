namespace DDDToolkit.Access;

/// <summary>
/// Marks an attribute class as a permission marker. A package declares its markers with it. A permission marker goes on
/// the member of a request that names a place, such as a unit or a resource. The marker's constructor takes the
/// permission that the caller holds at that place. The generator turns the marker into a call that makes the requirement
/// from the permission and the member. Membership declares its resource marker this way, and Tenancy its unit marker:
/// <code>
/// [PermissionMarker(typeof(MemberAccess), nameof(MemberAccess.On), IdOf = typeof(MemberAttribute&lt;,,,&gt;), Argument = 3, IdOfArgument = true)]
/// [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
/// public sealed class OnResourceAttribute(string permission) : Attribute
/// {
///     public string Permission { get; } = permission;
/// }
/// </code>
/// A request with the member <c>[OnResource(BoardPermissions.Edit)] BoardId Board</c> then answers
/// <see cref="IRequireAccess.RequiredAccess"/> with <c>MemberAccess.On(BoardPermissions.Edit, request.Board)</c>.
/// </summary>
/// <remarks>
/// The generator reads the permission from the marker's constructor argument, a string. It writes a call of the method
/// <see cref="Method"/> on <see cref="Factory"/>, with the permission and the marked member. Each member that a
/// <see cref="PermissionMarkerArgumentAttribute"/> marks goes into the same call, after the marked member. A marker on a
/// request's member without a permission is DDD00080. A marker goes on a positional parameter of a record or on a
/// property, so declare it for both, <c>AttributeTargets.Parameter | AttributeTargets.Property</c>. On a positional
/// parameter the marker applies to the parameter, and the generator reads it there. The generator also reads
/// <c>[property: OnResource(permission)]</c>.
/// <para>
/// Each marker is one requirement. When a request has several markers, the generator combines their calls with
/// <c>AccessRequirement.All</c>, in the order the markers are written. These calls come after the calls of the
/// requirement attributes on the request. A request may have two markers of one kind, such as two boards or two units.
/// <c>AttachAsset([OnResource(BoardPermissions.Edit)] BoardId Board, [OnResource(AssetPermissions.Use)] AssetId Asset)</c>
/// becomes <c>AccessRequirement.All(MemberAccess.On(BoardPermissions.Edit, request.Board), MemberAccess.On(AssetPermissions.Use, request.Asset))</c>.
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
    /// The template whose classes' ids the marked member may be, named as <c>[TemplateType]</c> names one. It is the open
    /// generic attribute that an application declares the class with, <c>typeof(OrganizationUnitAttribute&lt;&gt;)</c>. A
    /// member of another type is DDD00081. Without this diagnostic, a unit marker on a seat's id compiles and lets nobody
    /// through. Where the project sees no class declared with the template, the analyzer reports nothing, and the
    /// package's check refuses an id it does not know when the request is sent. Null to take any type the method takes.
    /// </summary>
    public Type? IdOf { get; set; }

    /// <summary>
    /// The type argument of <see cref="IdOf"/>, in the class's declaration, that names what the member is the id of. It
    /// is zero-based. The default, 0, is the class's own id. Membership's resource marker takes the fourth type argument,
    /// the resource that a member class is declared for, with <see cref="IdOfArgument"/>.
    /// </summary>
    public int Argument { get; set; }

    /// <summary>
    /// When <see cref="Argument"/> names an entity or an aggregate root, the member is the id that the class is declared
    /// with. <c>[Member&lt;DocumentShareId, UserId, NamedRole, Document&gt;]</c>
    /// with <c>Argument = 3, IdOfArgument = true</c> makes the member a <c>DocumentId</c>.
    /// </summary>
    public bool IdOfArgument { get; set; }

    /// <summary>
    /// The requirement attributes that the marker's permission already requires. One of these attributes beside the
    /// marker adds nothing. Only a seat in the tenant, or system work there, holds a permission at a unit of Tenancy's. So
    /// <c>[AtUnit]</c> names <c>[InTenant]</c>, and <c>[InTenant]</c> beside it is DDD00084, a warning. Empty by default.
    /// </summary>
    public Type[] Implies { get; set; } = [];

    /// <summary>
    /// The method of <see cref="Factory"/> that the caller's own way through must still pass at the marked member. A
    /// marker with this property allows <see cref="OrOwnAttribute"/> to pair with it. Set it on a marker whose member is
    /// what the request acts on. The method takes what <see cref="Method"/> takes: the permission, the marked member and
    /// the arguments beside it. Membership's <c>[OnResource]</c> names <c>MemberAccess.Sees</c>. So
    /// <c>LeaveCrew([OnResource(ProjectPermissionNames.ManageCrew)] ProjectId Project, [OrOwn] SeatId Seat)</c> becomes
    /// <c>Any(All(Own(request.Seat), MemberAccess.Sees(permission, request.Project)), MemberAccess.On(permission, request.Project))</c>.
    /// On both ways through, the project's check reads the project, compares the version the request names, and keeps the
    /// hold that the save is checked against.
    /// <para>
    /// The default is <see langword="null"/>. Leave it null on a marker of the place where a permission is checked, when
    /// the request acts on something else, as with Tenancy's <c>[AtUnit]</c>. With such a marker, a caller could send a
    /// unit where it holds the permission together with another person's seat. So <c>[OrOwn]</c> paired with such a
    /// marker is DDD00085.
    /// </para>
    /// </summary>
    public string? OwnAlternative { get; set; }
}
