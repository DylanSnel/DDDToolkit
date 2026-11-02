using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Marks the version of the resource that the caller last read, beside the request's only
/// <see cref="OnResourceAttribute"/>. The member is the third argument of <see cref="MemberAccess.On{TResourceId}"/>.
/// <code>
/// public sealed record ShareDocument(
///     [OnResource(DocumentPermissions.Share)] DocumentId Document, UserId With, [ExpectedVersion] long? ExpectedVersion = null) : IDocumentsRequest;
/// </code>
/// is <c>MemberAccess.On(DocumentPermissions.Share, request.Document, request.ExpectedVersion)</c>, so the check compares
/// it only once the caller may act, and a caller that may not is refused rather than told the resource moved on.
/// </summary>
/// <remarks>
/// It goes with one <see cref="OnResourceAttribute"/>. On a request without a resource marker it is DDD00080. A request
/// with markers on two or more kinds of resource uses <see cref="ExpectedVersionAttribute{TResource}"/>, which names the
/// resource. A request with two markers on one kind of resource implements <c>RequiredAccess</c>. Otherwise each of these
/// is DDD00080. The member is a <c>long?</c>, or a <c>long</c>, because the call's parameter takes either. Another type is
/// DDD00081.
/// </remarks>
[PermissionMarkerArgument(typeof(OnResourceAttribute))]
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
public sealed class ExpectedVersionAttribute : Attribute;
