using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Marks the resource on which the key of a request's <see cref="RequiresKeyAttribute"/> is held, through its members
/// or however else the resource's rules let a key be held: what <see cref="MemberAccess.On{TResourceId}"/> says,
/// written on the member of the request it reads.
/// <code>
/// [RequiresKey(DocumentKeys.Share)]
/// public sealed record ShareDocument([OnResource] DocumentId Document, UserId With, [ExpectedVersion] long? ExpectedVersion = null) : IDocumentsRequest;
/// </code>
/// is <c>MemberAccess.On(DocumentKeys.Share, request.Document, request.ExpectedVersion)</c>.
/// </summary>
/// <remarks>
/// It goes on a positional parameter of a record as it is, or on a property, and <c>[property: OnResource]</c> is read
/// as well. The member is the id of a resource with members: an aggregate root a class declared with
/// <c>[Member&lt;TId, TMemberId, TRoleId, TResource&gt;]</c> names as its resource. On another id it is DDD00081, where
/// the project sees such a class. <see cref="ExpectedVersionAttribute"/> beside it marks the version the caller read,
/// for a command that names one.
/// </remarks>
[KeyMarker(typeof(MemberAccess), nameof(MemberAccess.On), IdOf = typeof(MemberAttribute<,,,>), Argument = 3, IdOfArgument = true)]
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
public sealed class OnResourceAttribute : Attribute;
