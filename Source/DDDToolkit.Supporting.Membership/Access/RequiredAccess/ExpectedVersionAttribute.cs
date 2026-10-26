using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Marks the version of the resource the caller last read, beside <see cref="OnResourceAttribute"/>: the third
/// argument of <see cref="MemberAccess.On{TResourceId}"/>.
/// <code>
/// [RequiresPermission(DocumentPermissions.Share)]
/// public sealed record ShareDocument([OnResource] DocumentId Document, UserId With, [ExpectedVersion] long? ExpectedVersion = null) : IDocumentsRequest;
/// </code>
/// is <c>MemberAccess.On(DocumentPermissions.Share, request.Document, request.ExpectedVersion)</c>, so the check compares
/// it only once the caller may act, and a caller that may not is refused rather than told the resource moved on.
/// </summary>
/// <remarks>
/// It goes with <see cref="OnResourceAttribute"/> and nowhere else: on a request that marks no resource it is
/// DDD00080. The member is a <c>long?</c>, or a <c>long</c>, as the call's parameter takes; another type is DDD00081.
/// </remarks>
[PermissionMarkerArgument(typeof(OnResourceAttribute))]
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
public sealed class ExpectedVersionAttribute : Attribute;
