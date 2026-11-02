namespace DDDToolkit.Access;

/// <summary>
/// Marks the member of a request that names the caller's own value, beside the request's only permission. The access
/// check lets the caller through when the value is the caller's own, or when the caller holds the permission.
/// <code>
/// [RequiresPermission(TenancyPermissions.SeatsManage)]                                   // rename yourself, or any seat with the permission
/// public sealed record RenameSeat([OrOwn] SeatId Seat, string? DisplayName) : ITenantsRequest;
///
/// public sealed record LeaveCrew(                                                         // take yourself off, or manage the crew
///     [OnResource(ProjectPermissionNames.ManageCrew)] ProjectId Project, [OrOwn] SeatId Seat) : IProjectsRequest;
/// </code>
/// The first becomes <c>AccessRequirement.Any(AccessRequirement.Own(request.Seat), TenancyAccess.AtTenant("tenancy.seats.manage"))</c>.
/// Beside <c>[OnResource]</c>, the permission's case is the one on the resource. The caller's own way through then also
/// requires that the caller sees the resource: <c>Any(All(Own(request.Seat), MemberAccess.Sees(permission,
/// request.Project)), MemberAccess.On(permission, request.Project))</c>. The marker declares this with
/// <see cref="PermissionMarkerAttribute.OwnAlternative"/>.
/// </summary>
/// <remarks>
/// It pairs with one permission: the permission of <see cref="RequiresPermissionAttribute"/>, or the permission on a
/// marker. When a request has two or more permissions, use <see cref="OrOwnAttribute{TResource}"/>. Its type argument
/// names the resource whose permission the caller's own is the alternative of. The check tests the caller's own first,
/// because that reads nothing. It tests the permission only when the value is not the caller's. A caller who passes
/// neither is refused with one <c>access.refused</c>, which carries both reasons. The member's type needs an
/// <see cref="IOwnedByCurrentUser{TValue}"/>: a <see cref="Guid"/>, the caller's verified identity, from the core;
/// Tenancy's seat id; or a type of the application with a handle it registers. On another type it is DDD00086.
/// <para>
/// It does not pair with <c>[AtUnit]</c>. The unit names where the permission is checked, and the request acts on
/// something else. A caller could send a unit where it holds the permission together with another person's seat. A
/// permission held at one unit would then act on a seat anywhere. A request that needs this combination implements the
/// property <c>RequiredAccess</c>. Without a permission, the caller's own is no alternative to anything. A request about
/// the caller's own alone uses <see cref="OwnOnlyAttribute"/>, with <see cref="OwnAttribute"/> on the member. Each of
/// these mistakes is DDD00085. The attribute goes on a positional parameter of a record, or on a property. The generator
/// also reads <c>[property: OrOwn]</c>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
public sealed class OrOwnAttribute : Attribute;
