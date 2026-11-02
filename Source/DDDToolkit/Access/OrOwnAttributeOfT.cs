namespace DDDToolkit.Access;

/// <summary>
/// The form of <see cref="OrOwnAttribute"/> for a request with several permissions. Its type argument names the resource
/// whose permission the caller's own is the alternative of.
/// <code>
/// public sealed record DetachAsset(
///     [OnResource(BoardPermissions.Edit)] BoardId Board,
///     [OnResource(AssetPermissions.Use)] AssetId Asset,
///     [OrOwn&lt;Board&gt;] SeatId AttachedBy) : ICommand&lt;Board&gt;, IBoardsRequest;
/// </code>
/// becomes <c>AccessRequirement.All(AccessRequirement.Any(AccessRequirement.All(AccessRequirement.Own(request.AttachedBy),
/// MemberAccess.Sees(BoardPermissions.Edit, request.Board)), MemberAccess.On(BoardPermissions.Edit, request.Board)),
/// MemberAccess.On(AssetPermissions.Use, request.Asset))</c>. The seat that attached the asset may detach it from a board
/// that the seat sees. A caller with the permission to edit the board may detach it too. Every caller also needs the
/// permission to use the asset.
/// </summary>
/// <remarks>
/// <typeparamref name="TResource"/> is the class whose id the member of one marker is, an aggregate root such as
/// <c>Board</c>. The caller's own is the alternative of that marker's permission only. The generator puts the
/// <c>Any</c> in the place of that marker among the request's requirements, in the shape that
/// <see cref="PermissionMarkerAttribute.OwnAlternative"/> declares. The generic form is required beside two or more
/// permissions, and allowed beside one. These are DDD00085: a class that no marker of the request is on, two markers on
/// that class, and a marker without <c>OwnAlternative</c>, such as Tenancy's <c>[AtUnit]</c>.
/// </remarks>
/// <typeparam name="TResource">The class whose id the member of the marker it pairs with is.</typeparam>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
public sealed class OrOwnAttribute<TResource> : Attribute
    where TResource : class;
