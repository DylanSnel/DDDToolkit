namespace DDDToolkit.Access;

/// <summary>
/// Says on a request that it is about the caller's own alone: the member marked <see cref="OwnAttribute"/> names a value
/// the caller has, and no key stands in for it.
/// <code>
/// [OwnOnly]
/// public sealed record LeaveTenant([Own] SeatId Seat) : ITenantsRequest;
/// </code>
/// is <c>AccessRequirement.Own(request.Seat)</c>. Anyone else is refused with <c>access.not-own</c>.
/// </summary>
/// <remarks>
/// A request about the caller's own that another may send with a key says <see cref="RequiresKeyAttribute"/> and marks the
/// member <see cref="OrOwnAttribute"/> instead. Stacked beside another requirement attribute it holds as well as that one
/// (<see cref="AccessRequirement.All"/>), in the order they are written. Without a member marked <see cref="OwnAttribute"/>
/// it says nothing, and a marked member without it is read by nothing: each is DDD00085.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class OwnOnlyAttribute : Attribute;
