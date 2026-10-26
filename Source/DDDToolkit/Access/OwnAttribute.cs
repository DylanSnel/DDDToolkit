namespace DDDToolkit.Access;

/// <summary>
/// Marks the member of a request marked <see cref="OwnOnlyAttribute"/> that names the caller's own: the value the
/// caller's is compared with at the door.
/// <code>
/// [OwnOnly]
/// public sealed record LeaveTenant([Own] SeatId Seat) : ITenantsRequest;
/// </code>
/// </summary>
/// <remarks>
/// The member is of a type something says the caller's own of (<see cref="ICallersOwn{TValue}"/>), as for
/// <see cref="OrOwnAttribute"/>; on another it is DDD00086, and without <see cref="OwnOnlyAttribute"/> on the request,
/// or twice on one, DDD00085. It goes on a positional parameter of a record as it is, or on a property, and
/// <c>[property: Own]</c> is read as well.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
public sealed class OwnAttribute : Attribute;
