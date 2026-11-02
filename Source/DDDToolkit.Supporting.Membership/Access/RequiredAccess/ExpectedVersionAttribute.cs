using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Marks the version of the resource the caller last read, beside the one <see cref="OnResourceAttribute"/> of the request:
/// the third argument of <see cref="MemberAccess.On{TResourceId}"/>.
/// <code>
/// public sealed record ShareDocument(
///     [OnResource(DocumentPermissions.Share)] DocumentId Document, UserId With, [ExpectedVersion] long? ExpectedVersion = null) : IDocumentsRequest;
/// </code>
/// is <c>MemberAccess.On(DocumentPermissions.Share, request.Document, request.ExpectedVersion)</c>, so the check compares
/// it only once the caller may act, and a caller that may not is refused rather than told the resource moved on.
/// </summary>
/// <remarks>
/// It goes with exactly one <see cref="OnResourceAttribute"/>. On a request that marks no resource it is DDD00080. Beside
/// markers on two or more kinds of resource, <see cref="ExpectedVersionAttribute{TResource}"/> names the resource whose
/// version it is, and beside two markers on one kind of resource the request writes <c>RequiredAccess</c>: each is
/// DDD00080 otherwise. The member is a <c>long?</c>, or a <c>long</c>, as the call's parameter takes; another type is
/// DDD00081.
/// </remarks>
[PermissionMarkerArgument(typeof(OnResourceAttribute))]
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
public sealed class ExpectedVersionAttribute : Attribute;
