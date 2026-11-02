using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Marks the resource on which the caller of a request holds a permission, through its members or however else the
/// resource's rules let a permission be held. It is what <see cref="MemberAccess.On{TResourceId}"/> says, written on the
/// member of the request it reads.
/// <code>
/// public sealed record ShareDocument(
///     [OnResource(DocumentPermissions.Share)] DocumentId Document, UserId With, [ExpectedVersion] long? ExpectedVersion = null) : IDocumentsRequest;
/// </code>
/// is <c>MemberAccess.On(DocumentPermissions.Share, request.Document, request.ExpectedVersion)</c>.
/// </summary>
/// <remarks>
/// It goes on a positional parameter of a record as it is, or on a property, and <c>[property: OnResource(permission)]</c>
/// is read as well. The member is the id of a resource with members: an aggregate root a class declared with
/// <c>[Member&lt;TId, TMemberId, TRoleId, TResource&gt;]</c> names as its resource. On another id it is DDD00081, where the
/// project sees such a class. The permission is one declared on that resource, <c>[On&lt;TResource&gt;]</c>, and a permission
/// declared on another resource only is DDD00093. <see cref="ExpectedVersionAttribute"/> beside it marks the version the
/// caller read, for a command that names one.
/// <para>
/// Several on one request are all required, each on its own resource. Attaching an asset to a board needs a permission on
/// each:
/// <code>
/// public sealed record AttachAsset(
///     [OnResource(BoardPermissions.Edit)] BoardId Board,
///     [OnResource(AssetPermissions.Use)] AssetId Asset,
///     [ExpectedVersion&lt;Board&gt;] long? Version = null) : ICommand&lt;Board&gt;, IBoardsRequest;
/// </code>
/// is <c>AccessRequirement.All(MemberAccess.On(BoardPermissions.Edit, request.Board, request.Version), MemberAccess.On(AssetPermissions.Use,
/// request.Asset))</c>. Each check decides its own resource, keeps a hold on it, and refuses under that resource's codes.
/// </para>
/// <para>
/// The resource is what the request acts on, so <see cref="OrOwnAttribute"/> pairs with it: the request is about the
/// caller's own there, or the caller holds the permission on the resource.
/// <code>
/// public sealed record LeaveCrew(                                                          // take yourself off, or manage the crew
///     [OnResource(ProjectPermissionNames.ManageCrew)] ProjectId Project, [OrOwn] SeatId Seat) : IProjectsRequest;
/// </code>
/// is <c>AccessRequirement.Any(AccessRequirement.All(AccessRequirement.Own(request.Seat), MemberAccess.Sees(ProjectPermissionNames.ManageCrew,
/// request.Project)), MemberAccess.On(ProjectPermissionNames.ManageCrew, request.Project))</c>. Either way the project's check
/// reads the project, a caller on its own needs only to see it, compares the version <see cref="ExpectedVersionAttribute"/>
/// marks, and keeps what the expert hold holds the save to. The handler still finds the seat on that resource, and
/// refuses one that is not there: the caller's own lets it act on itself, not on a resource it has nothing to do with.
/// </para>
/// <para>
/// Where a permission on the resource manages the members (<see cref="MembershipRules.ManagesMembersPermission"/>), a
/// database that checks every row lets a member row change only for a caller that holds that permission, so a caller who takes
/// itself off without it is refused there. The handler then saves the caller's own place as the application's own work,
/// begun for that caller once its request is the one in hand (<see cref="RequestInHand"/>): the check decided it.
/// </para>
/// </remarks>
/// <param name="permission">The permission the caller holds on the resource: a constant declared <c>[On&lt;TResource&gt;]</c>.</param>
[PermissionMarker(typeof(MemberAccess), nameof(MemberAccess.On), IdOf = typeof(MemberAttribute<,,,>), Argument = 3, IdOfArgument = true, OwnAlternative = nameof(MemberAccess.Sees))]
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
public sealed class OnResourceAttribute(string permission) : Attribute
{
    /// <summary>The permission the caller holds on the resource.</summary>
    public string Permission { get; } = permission;
}
