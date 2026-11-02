using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// <see cref="ExpectedVersionAttribute"/> for a request with markers on several resources: it names the resource whose
/// version the caller last read.
/// <code>
/// public sealed record AttachAsset(
///     [OnResource(BoardPermissions.Edit)] BoardId Board,
///     [OnResource(AssetPermissions.Use)] AssetId Asset,
///     [ExpectedVersion&lt;Board&gt;] long? Version = null) : ICommand&lt;Board&gt;, IBoardsRequest;
/// </code>
/// is <c>AccessRequirement.All(MemberAccess.On(BoardPermissions.Edit, request.Board, request.Version), MemberAccess.On(AssetPermissions.Use, request.Asset))</c>.
/// The board's check compares the version, and the asset's is only read.
/// </summary>
/// <remarks>
/// <typeparamref name="TResource"/> is the resource class, the aggregate root a member class names, such as <c>Board</c>.
/// The version goes with the <see cref="OnResourceAttribute"/> on that resource's id. It is required beside markers on
/// two or more kinds of resource, and allowed beside one. A resource no <see cref="OnResourceAttribute"/> of the request is
/// on, and two markers on that one resource, are each DDD00080.
/// </remarks>
/// <typeparam name="TResource">The resource whose version the member is.</typeparam>
[PermissionMarkerArgument(typeof(OnResourceAttribute))]
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
public sealed class ExpectedVersionAttribute<TResource> : Attribute
    where TResource : class;
