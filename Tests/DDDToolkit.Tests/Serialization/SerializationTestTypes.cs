using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;

namespace DDDToolkit.Tests.Serialization;

/// <summary>
/// A struct id with a prefix: its textual form (and therefore a dictionary key) is <c>TCK_{guid}</c> while
/// its JSON value stays the bare guid. <c>CatId</c> from the example library covers the unprefixed case.
/// </summary>
[EntityId<Guid>("TCK")]
public readonly partial record struct TicketId;

/// <summary>A struct id over something that is not a guid, so the JSON value is a number rather than a string.</summary>
[EntityId<int>]
public readonly partial record struct SeatNumber;

/// <summary>
/// A hand written struct id: no generator, and therefore no <c>[JsonConverter]</c> attribute to defer to.
/// The System.Text.Json factory has to build a converter for it itself.
/// </summary>
public readonly record struct SectionId(string Value) : IEntityId<string>;

/// <summary>
/// A hand written reference type id: it implements <see cref="IEntityId{TValue}"/> without deriving from
/// <c>SingleValueObject&lt;T&gt;</c>, so neither the single value object converter nor a generated one fits it.
/// </summary>
public sealed record VenueId(string Value) : IEntityId<string>;

/// <summary>Raised by <see cref="TicketOrder"/>; only used to prove the event list stays out of the document.</summary>
public sealed record TicketOrderPlaced(TicketId TicketId) : DomainEvent;

/// <summary>
/// A small aggregate root that carries a struct id, a single value object and a multi property value
/// object, so serializing it exercises the converters and the contract resolver together.
/// </summary>
[AggregateRoot<TicketId>]
public partial class TicketOrder
{
    public TicketOrder(TicketId id, PersonName buyer, EmailAddress email, IEnumerable<SeatNumber> seats) : base(id)
    {
        Buyer = buyer;
        Email = email;
        _seats.AddRange(seats);
        RaiseDomainEvent(new TicketOrderPlaced(id));
    }

    public PersonName Buyer { get; private set; }

    public EmailAddress Email { get; private set; }

    /// <summary>Read-only outside the aggregate; backed by the generated <c>_seats</c> list.</summary>
    public partial IReadOnlyList<SeatNumber> Seats { get; }
}

/// <summary>A DTO holding one of everything: struct ids, a nullable struct id, a class id and value objects.</summary>
public sealed record TicketDto(
    CatId Cat,
    CatId? MaybeCat,
    TicketId Ticket,
    SeatNumber Seat,
    SectionId Section,
    PersonId Person,
    EmailAddress Email,
    PersonName Name);
