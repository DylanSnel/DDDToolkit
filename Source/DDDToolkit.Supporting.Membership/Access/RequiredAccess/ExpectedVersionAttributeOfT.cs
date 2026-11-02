using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// The form of <see cref="ExpectedVersionAttribute"/> for a request with markers on several resources. Its type argument
/// names the resource whose version the caller last read.
/// <code>
/// public sealed record AttachAsset(
///     [OnResource(BoardPermissions.Edit)] BoardId Board,
///     [OnResource(AssetPermissions.Use)] AssetId Asset,
///     [ExpectedVersion&lt;Board&gt;] long? Version = null) : ICommand&lt;Board&gt;, IBoardsRequest;
/// </code>
/// becomes <c>AccessRequirement.All(MemberAccess.On(BoardPermissions.Edit, request.Board, request.Version), MemberAccess.On(AssetPermissions.Use, request.Asset))</c>.
/// The board's check compares the version. The asset's check only reads the asset.
/// </summary>
/// <remarks>
/// <typeparamref name="TResource"/> is the resource class, the aggregate root that a member class names, such as
/// <c>Board</c>. The version belongs to the <see cref="OnResourceAttribute"/> on that resource's id. The generic form is
/// required beside markers on two or more kinds of resource, and allowed beside one. These are DDD00080: a resource that
/// no <see cref="OnResourceAttribute"/> of the request is on, and two markers on that resource.
/// </remarks>
/// <typeparam name="TResource">The resource whose version the member holds.</typeparam>
[PermissionMarkerArgument(typeof(OnResourceAttribute))]
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
public sealed class ExpectedVersionAttribute<TResource> : Attribute
    where TResource : class;
