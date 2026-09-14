using DDDToolkit.ExampleLibrary.Common.ValueObjects;

namespace DDDToolkit.HotChocolate.Tests.Domain;

/// <summary>
/// The query root of the schema the tests execute against. Every field is here to pin down one part
/// of the DDDToolkit-to-GraphQL mapping: a struct id, a class id, its always-valid twin, a single
/// value object with an explicit schema type, lists, nullability, arguments and input objects.
/// </summary>
public sealed class Query
{
    /// <summary>A struct id from the example library; binds to <c>UUID</c>.</summary>
    public CatId Cat() => TestData.Cat;

    /// <summary>The same id, nullable and present.</summary>
    public CatId? NullableCat() => TestData.Cat;

    /// <summary>The same id, nullable and absent.</summary>
    public CatId? MissingCat() => null;

    /// <summary>A list of struct ids; binds to <c>[UUID!]!</c>.</summary>
    public List<CatId> Cats() => [TestData.Cat, TestData.OtherCat];

    /// <summary>A reference (class) id; binds to <c>UUID</c> as well.</summary>
    public PersonId Person() => TestData.Person;

    /// <summary>The always-valid twin of the reference id, bound to the same scalar.</summary>
    public ValidPersonId ValidPerson() => TestData.Person.ToValid();

    /// <summary>A single value object carrying <c>[GraphQLType&lt;EmailAddressType&gt;]</c>.</summary>
    public EmailAddress Email() => TestData.Holder;

    /// <summary>A struct id declared in this assembly, whose <c>ToString()</c> prefix is "TST".</summary>
    public TicketId PrefixedId() => TestData.Ticket;

    /// <summary>A struct id over an int; binds to <c>Int</c>.</summary>
    public SeatNumber Seat() => TestData.Seat;

    /// <summary>A struct id that names its own schema type with <c>[GraphQLType&lt;T&gt;]</c>.</summary>
    public LoginId Login() => TestData.Login;

    // ---------------------------------------------------------------- arguments

    /// <summary>Returns the id it was given, proving a struct id survives the round trip.</summary>
    public CatId EchoCatId(CatId id) => id;

    /// <summary>
    /// Returns <c>ToString()</c> of the argument. A string argument would come back without the
    /// "TST_" prefix, so this shows the resolver really received a <see cref="TicketId"/>.
    /// </summary>
    public string DescribeTicketId(TicketId id) => id.ToString();

    /// <summary>Returns the ids read out of an input object, in their <c>ToString()</c> form.</summary>
    public string DescribeReservation(SeatReservation reservation)
        => $"{reservation.Ticket}/{reservation.Seat.Value}";

    // ---------------------------------------------------------------- object types

    /// <summary>An aggregate root; its pending <c>DomainEvents</c> must not reach the schema.</summary>
    public Ticket IssuedTicket() => new(TestData.Ticket, TestData.Holder, TestData.Seat);

    /// <summary>A value object as an object type; its validation members must not reach the schema.</summary>
    public PersonName HolderName() => new("Ada", "Lovelace");

    /// <summary>A concrete domain event, so the <c>DomainEvent</c> interface has an implementation.</summary>
    public TicketIssued LatestEvent() => new()
    {
        TicketId = TestData.Ticket,
        EventId = TestData.EventGuid,
        OccurredAt = TestData.OccurredAt,
    };
}

/// <summary>An input object holding two strongly typed struct ids.</summary>
public sealed class SeatReservation
{
    /// <summary>The ticket being reserved.</summary>
    public TicketId Ticket { get; set; }

    /// <summary>The seat being reserved.</summary>
    public SeatNumber Seat { get; set; }
}
