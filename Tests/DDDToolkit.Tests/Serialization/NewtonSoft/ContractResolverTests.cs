using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Interfaces;
using DDDToolkit.NewtonSoft.Json;
using DDDToolkit.NewtonSoft.Json.Converters;
using FluentAssertions;
using Newtonsoft.Json;

namespace DDDToolkit.Tests.Serialization.NewtonSoft;

/// <summary>
/// What the contract resolver is for: the bookkeeping the base types carry - an aggregate's pending events,
/// a value object's validation flags - is marked <c>[Internal]</c> and must not end up in the document.
/// </summary>
public class ContractResolverTests
{
    private readonly JsonSerializerSettings _settings = new JsonSerializerSettings().AddDDDToolkitConverters();

    private static TicketOrder AnOrder() => new(
        TicketId.CreateUnique(),
        new PersonName("John", "Doe"),
        EmailAddress.Create("john@example.com"),
        [new SeatNumber(1), new SeatNumber(2)]);

    [Fact]
    public void AggregateRoot_KeepsItsInternalMembersOutOfTheDocument()
    {
        var order = AnOrder();

        var json = JsonConvert.SerializeObject(order, _settings);

        json.Should().NotContain("DomainEvents")
            .And.NotContain("IsValid")
            .And.NotContain("IsValidated");
    }

    [Fact]
    public void AggregateRoot_WritesItsState()
    {
        var order = AnOrder();

        var json = JsonConvert.SerializeObject(order, _settings);

        json.Should().Contain($"\"Id\":\"{order.Id.Value}\"")
            .And.Contain("\"Email\":\"john@example.com\"")
            .And.Contain("\"FirstName\":\"John\"")
            .And.Contain("\"Seats\":[1,2]")
            .And.Contain("\"Version\":0");
    }

    [Fact]
    public void AggregateRoot_StillHoldsTheEventsThatWereHiddenFromTheDocument()
    {
        // Hidden from the serializer, not dropped: the persistence layer still drains them.
        var order = AnOrder();

        JsonConvert.SerializeObject(order, _settings);

        ((IHasDomainEvents)order).DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TicketOrderPlaced>();
    }

    [Fact]
    public void WithoutTheResolver_TheInternalMembersWouldBeWritten()
    {
        // The contrast that shows the resolver is doing the work.
        var settings = new JsonSerializerSettings { Converters = { new SingleValueObjectConverter() } };

        var json = JsonConvert.SerializeObject(AnOrder(), settings);

        json.Should().Contain("DomainEvents");
    }

    [Fact]
    public void ValueObject_HidesItsValidationFlagsButKeepsItsValues()
    {
        var json = JsonConvert.SerializeObject(new PersonName("John", "Ronald", "Doe"), _settings);

        json.Should().NotContain("IsValid").And.NotContain("IsValidated");
        json.Should().Contain("\"MiddleNames\":\"Ronald\"");
    }

    [Fact]
    public void ValueObject_RoundTripsThroughItsNonPublicSetters()
    {
        var name = new PersonName("John", "Ronald", "Doe");

        var restored = JsonConvert.DeserializeObject<PersonName>(JsonConvert.SerializeObject(name, _settings), _settings);

        restored.Should().Be(name);
        restored!.MiddleNames.Should().Be("Ronald");
    }

    [Fact]
    public void StructId_IsLeftToItsConverter()
    {
        // The resolver must not trip over a value type on the way past.
        var json = JsonConvert.SerializeObject(new { Ticket = TicketId.CreateUnique(), Seat = new SeatNumber(4) }, _settings);

        json.Should().Contain("\"Seat\":4");
    }
}
