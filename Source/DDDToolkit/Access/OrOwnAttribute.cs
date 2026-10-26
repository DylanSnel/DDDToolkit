namespace DDDToolkit.Access;

/// <summary>
/// Marks the member of a request that names the caller's own, beside <see cref="RequiresPermissionAttribute"/>: the request is
/// about the caller's own, or the caller holds the permission. Yours, or the permission.
/// <code>
/// [RequiresPermission(TenancyPermissions.SeatsManage)]                                   // rename yourself, or any seat with the permission
/// public sealed record RenameSeat([OrOwn] SeatId Seat, string? DisplayName) : ITenantsRequest;
///
/// [RequiresPermission(ProjectPermissionNames.ManageCrew)]                                    // take yourself off, or manage the crew
/// public sealed record LeaveCrew([OnResource] ProjectId Project, [OrOwn] SeatId Seat) : IProjectsRequest;
/// </code>
/// is <c>AccessRequirement.Any(AccessRequirement.Own(request.Seat), TenancyAccess.AtTenant("tenancy.seats.manage"))</c>.
/// With <c>[OnResource]</c> the permission's case is the one on the resource, and the caller's own way through is held to the
/// resource as well, which the caller must see: <c>Any(All(Own(request.Seat), MemberAccess.Sees(permission, request.Project)),
/// MemberAccess.On(permission, request.Project))</c>, as the marker says with <see cref="PermissionMarkerAttribute.OwnAlternative"/>.
/// </summary>
/// <remarks>
/// The caller's own is asked first, since it reads nothing, and the permission only where it is not. A caller for whom neither
/// holds is refused with one <c>access.refused</c>, which carries both reasons. The member is of a type something says
/// the caller's own of (<see cref="ICallersOwn{TValue}"/>): a <see cref="Guid"/>, the caller's verified identity, of the
/// core's; Tenancy's seat id; an application's own kind with a handle it registers. On another type it is DDD00086.
/// <para>
/// Not beside <c>[AtUnit]</c>: the unit says where the permission is checked, and a caller who may act on its own would send
/// the unit the permission is checked at for another subject, so that a permission held at one unit would act on a seat anywhere. A
/// request that needs that writes the property, <c>RequiredAccess</c>. Without <see cref="RequiresPermissionAttribute"/> there
/// is no permission to be the alternative of: a request about the caller's own alone says <see cref="OwnOnlyAttribute"/>, with
/// <see cref="OwnAttribute"/> on the member. Each is DDD00085 otherwise. It goes on a positional parameter of a record as
/// it is, or on a property, and <c>[property: OrOwn]</c> is read as well.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
public sealed class OrOwnAttribute : Attribute;
