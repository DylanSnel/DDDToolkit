using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Marks the resource on which the caller of a request holds a permission. The caller holds it through the resource's
/// members, or in another way that the resource's rules allow. The generator turns the marker into a call of
/// <see cref="MemberAccess.On{TResourceId}"/> on the marked member.
/// <code>
/// public sealed record ShareDocument(
///     [OnResource(DocumentPermissions.Share)] DocumentId Document, UserId With, [ExpectedVersion] long? ExpectedVersion = null) : IDocumentsRequest;
/// </code>
/// is <c>MemberAccess.On(DocumentPermissions.Share, request.Document, request.ExpectedVersion)</c>.
/// </summary>
/// <remarks>
/// It goes on a positional parameter of a record, or on a property. The generator also reads
/// <c>[property: OnResource(permission)]</c>. The member is the id of a resource with members: an aggregate root that a
/// class declared with <c>[Member&lt;TId, TMemberId, TRoleId, TResource&gt;]</c> names as its resource. On another id it is
/// DDD00081, in a project that sees such a class. The permission is declared on that resource with
/// <c>[On&lt;TResource&gt;]</c>. A permission declared only on another resource is DDD00093.
/// <see cref="ExpectedVersionAttribute"/> beside it marks the version the caller read, for a command that names one.
/// <para>
/// A request can have several of these markers, each on its own resource. All of them are required. Attaching an asset to
/// a board needs a permission on each:
/// <code>
/// public sealed record AttachAsset(
///     [OnResource(BoardPermissions.Edit)] BoardId Board,
///     [OnResource(AssetPermissions.Use)] AssetId Asset,
///     [ExpectedVersion&lt;Board&gt;] long? Version = null) : ICommand&lt;Board&gt;, IBoardsRequest;
/// </code>
/// becomes <c>AccessRequirement.All(MemberAccess.On(BoardPermissions.Edit, request.Board, request.Version), MemberAccess.On(AssetPermissions.Use,
/// request.Asset))</c>. Each <c>MemberAccess.On</c> call checks its own resource, keeps a hold on that resource, and
/// refuses with that resource's refusal codes.
/// </para>
/// <para>
/// The member can be a collection of ids of one resource, any type that implements <c>IEnumerable&lt;TId&gt;</c>, such as
/// an array, a list or a set. The caller then holds the permission on each resource:
/// <code>
/// public sealed record ArchiveBoards([OnResource(BoardPermissions.Edit)] IReadOnlyCollection&lt;BoardId&gt; Boards) : ICommand, IBoardsRequest;
/// </code>
/// becomes <c>MemberAccess.OnEach(BoardPermissions.Edit, request.Boards)</c>. The check reads the resources in one
/// statement for every 200 ids. One resource that is refused refuses the request, with <c>not-found</c> before
/// <c>not-permitted</c>, and the refusal names no resource. An empty collection lets a caller through that the questions
/// can be asked about. <see cref="ExpectedVersionAttribute"/> and <see cref="OrOwnAttribute"/> do not pair with a marker on
/// a collection (DDD00109).
/// </para>
/// <para>
/// The resource is what the request acts on, so <see cref="OrOwnAttribute"/> can pair with the marker. The caller is let
/// through when the request is about the caller's own on that resource, or when the caller holds the permission on the
/// resource.
/// <code>
/// public sealed record LeaveCrew(                                                          // take yourself off, or manage the crew
///     [OnResource(ProjectPermissionNames.ManageCrew)] ProjectId Project, [OrOwn] SeatId Seat) : IProjectsRequest;
/// </code>
/// becomes <c>AccessRequirement.Any(AccessRequirement.All(AccessRequirement.Own(request.Seat), MemberAccess.Sees(ProjectPermissionNames.ManageCrew,
/// request.Project)), MemberAccess.On(ProjectPermissionNames.ManageCrew, request.Project))</c>. On both ways through, the
/// project's check reads the project. A caller on the own way through only needs to see the project. The check compares
/// the version that <see cref="ExpectedVersionAttribute"/> marks, and keeps the hold that the expert hold checks the save
/// against. The handler still looks up the seat on that resource, and refuses a seat that is not there. So the caller's
/// own way through covers the caller's own place on that resource only.
/// </para>
/// <para>
/// Where a permission on the resource manages the members (<see cref="MembershipRules.ManagesMembersPermission"/>), a
/// database that checks every row lets a member row change only for a caller that holds that permission, so a caller who takes
/// itself off without it is refused there. The handler then saves the caller's own place as the application's own work,
/// begun for that caller once its request is the one in hand (<see cref="RequestInHand"/>): the check decided it.
/// </para>
/// </remarks>
/// <param name="permission">The permission the caller holds on the resource: a constant declared <c>[On&lt;TResource&gt;]</c>.</param>
[PermissionMarker(typeof(MemberAccess), nameof(MemberAccess.On), IdOf = typeof(MemberAttribute<,,,>), Argument = 3, IdOfArgument = true, OwnAlternative = nameof(MemberAccess.Sees), EachMethod = nameof(MemberAccess.OnEach))]
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
public sealed class OnResourceAttribute(string permission) : Attribute
{
    /// <summary>The permission the caller holds on the resource.</summary>
    public string Permission { get; } = permission;
}
