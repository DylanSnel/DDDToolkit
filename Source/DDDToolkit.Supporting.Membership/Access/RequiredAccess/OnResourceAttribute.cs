using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Marks the resource on which the permission of a request's <see cref="RequiresPermissionAttribute"/> is held, through its members
/// or however else the resource's rules let a permission be held: what <see cref="MemberAccess.On{TResourceId}"/> says,
/// written on the member of the request it reads.
/// <code>
/// [RequiresPermission(DocumentPermissions.Share)]
/// public sealed record ShareDocument([OnResource] DocumentId Document, UserId With, [ExpectedVersion] long? ExpectedVersion = null) : IDocumentsRequest;
/// </code>
/// is <c>MemberAccess.On(DocumentPermissions.Share, request.Document, request.ExpectedVersion)</c>.
/// </summary>
/// <remarks>
/// It goes on a positional parameter of a record as it is, or on a property, and <c>[property: OnResource]</c> is read
/// as well. The member is the id of a resource with members: an aggregate root a class declared with
/// <c>[Member&lt;TId, TMemberId, TRoleId, TResource&gt;]</c> names as its resource. On another id it is DDD00081, where
/// the project sees such a class. <see cref="ExpectedVersionAttribute"/> beside it marks the version the caller read,
/// for a command that names one.
/// <para>
/// The resource is what the request acts on, so <see cref="OrOwnAttribute"/> goes beside it: the request is about the
/// caller's own there, or the caller holds the permission on the resource.
/// <code>
/// [RequiresPermission(ProjectPermissionNames.ManageCrew)]                                    // take yourself off, or manage the crew
/// public sealed record LeaveCrew([OnResource] ProjectId Project, [OrOwn] SeatId Seat) : IProjectsRequest;
/// </code>
/// is <c>AccessRequirement.Any(AccessRequirement.All(AccessRequirement.Own(request.Seat), MemberAccess.Sees(ProjectPermissionNames.ManageCrew,
/// request.Project)), MemberAccess.On(ProjectPermissionNames.ManageCrew, request.Project))</c>. Either way the project's check
/// reads the project, a caller on its own needs only to see it, compares the version <see cref="ExpectedVersionAttribute"/>
/// marks, and keeps what the expert hold holds the save to. The handler still finds the seat on that resource, and
/// refuses one that is not: the caller's own lets it act on itself, not on a resource it has nothing to do with.
/// </para>
/// <para>
/// Where a permission on the resource locks the members (<see cref="MembershipRules.MembersLockPermission"/>), a
/// database that checks every row lets a member row change only for a caller that holds that permission, so a caller who takes
/// itself off without it is refused there. The handler then saves the caller's own place as the application's own work,
/// begun for that caller once its request is the one in hand (<see cref="RequestInHand"/>): the check decided it.
/// </para>
/// </remarks>
[PermissionMarker(typeof(MemberAccess), nameof(MemberAccess.On), IdOf = typeof(MemberAttribute<,,,>), Argument = 3, IdOfArgument = true, OwnAlternative = nameof(MemberAccess.Sees))]
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
public sealed class OnResourceAttribute : Attribute;
