using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;

namespace DDDToolkit.HotChocolate.Tests.Domain;

/// <summary>
/// An aggregate root exposed to GraphQL as an object type. It exists to prove what the schema shows
/// of a root: <c>id</c> and <c>version</c> are API, the pending <c>DomainEvents</c> are not.
/// </summary>
[AggregateRoot<TicketId>]
public partial class Ticket
{
    /// <summary>Issues a ticket, raising <see cref="TicketIssued"/>.</summary>
    public Ticket(TicketId id, EmailAddress holder, SeatNumber seat) : base(id)
    {
        Holder = holder;
        Seat = seat;
        RaiseDomainEvent(new TicketIssued { TicketId = id });
    }

    /// <summary>Who the ticket belongs to.</summary>
    public EmailAddress Holder { get; private set; } = default!;

    /// <summary>The seat it is good for.</summary>
    public SeatNumber Seat { get; private set; }
}

/// <summary>Raised when a ticket is issued.</summary>
public sealed record TicketIssued : DomainEvent
{
    /// <summary>The ticket that was issued.</summary>
    public required TicketId TicketId { get; init; }
}
