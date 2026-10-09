using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.TestHost.Contracts;

namespace DDDToolkit.Supporting.Tenancy.TestHost.Domain;

/// <summary>
/// The application welcomed a person to their seat: an event of the application's own, raised by its own seat class,
/// as a host raises one to send a welcome message. Tenancy knows nothing of it.
/// </summary>
/// <param name="SeatId">The seat.</param>
[DomainEventName("host.seat-welcomed")]
public sealed record HostSeatWelcomed(SeatId SeatId) : DomainEvent;
