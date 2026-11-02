namespace DDDToolkit.Access;

/// <summary>
/// <see cref="OrOwnAttribute"/> for a request with several permissions: it names the resource whose permission the caller's
/// own is the alternative of.
/// <code>
/// public sealed record DetachAsset(
///     [OnResource(BoardPermissions.Edit)] BoardId Board,
///     [OnResource(AssetPermissions.Use)] AssetId Asset,
///     [OrOwn&lt;Board&gt;] SeatId AttachedBy) : ICommand&lt;Board&gt;, IBoardsRequest;
/// </code>
/// is <c>AccessRequirement.All(AccessRequirement.Any(AccessRequirement.All(AccessRequirement.Own(request.AttachedBy),
/// MemberAccess.Sees(BoardPermissions.Edit, request.Board)), MemberAccess.On(BoardPermissions.Edit, request.Board)),
/// MemberAccess.On(AssetPermissions.Use, request.Asset))</c>. The caller who attached the asset detaches it from a board it
/// sees, or whoever may edit the board does, and either way the caller uses the asset.
/// </summary>
/// <remarks>
/// <typeparamref name="TResource"/> is the class whose id the member of one marker is, an aggregate root such as
/// <c>Board</c>. The caller's own pairs with that marker's permission only, in the place the marker has among the
/// request's requirements, with the shape the marker gives it (<see cref="PermissionMarkerAttribute.OwnAlternative"/>).
/// It is required beside two or more permissions and allowed beside one. A class no marker of the request is on, two
/// markers on that class, and a marker without <c>OwnAlternative</c>, Tenancy's <c>[AtUnit]</c>, are each DDD00085.
/// </remarks>
/// <typeparam name="TResource">The class whose id the member of the marker it pairs with is.</typeparam>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
public sealed class OrOwnAttribute<TResource> : Attribute
    where TResource : class;
