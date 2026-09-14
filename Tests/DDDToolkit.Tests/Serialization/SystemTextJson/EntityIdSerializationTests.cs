using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Serialization;
using FluentAssertions;
using System.Text.Json;

namespace DDDToolkit.Tests.Serialization.SystemTextJson;

/// <summary>
/// Strongly typed ids through System.Text.Json: an id is written as the value it wraps, comes back as the
/// same id, and works everywhere a plain value works - in a list, as a dictionary key, inside a DTO.
/// </summary>
public class EntityIdSerializationTests
{
    private readonly JsonSerializerOptions _options = new JsonSerializerOptions().AddDDDToolkitConverters();

    [Fact]
    public void StructId_IsWrittenAsItsUnderlyingValue()
    {
        var id = CatId.CreateUnique();

        var json = JsonSerializer.Serialize(id, _options);

        json.Should().Be($"\"{id.Value}\"");
    }

    [Fact]
    public void StructId_RoundTrips()
    {
        var id = CatId.CreateUnique();

        var restored = JsonSerializer.Deserialize<CatId>(JsonSerializer.Serialize(id, _options), _options);

        restored.Should().Be(id);
        restored.Value.Should().Be(id.Value);
    }

    [Fact]
    public void StructId_IsReadFromABareValue()
    {
        var value = Guid.NewGuid();

        var id = JsonSerializer.Deserialize<CatId>($"\"{value}\"", _options);

        id.Should().Be(new CatId(value));
    }

    [Fact]
    public void PrefixedStructId_WritesTheValueWithoutThePrefix()
    {
        var id = TicketId.CreateUnique();

        var json = JsonSerializer.Serialize(id, _options);

        json.Should().Be($"\"{id.Value}\"");
        JsonSerializer.Deserialize<TicketId>(json, _options).Should().Be(id);
    }

    [Fact]
    public void IntStructId_IsWrittenAsANumber()
    {
        var seat = new SeatNumber(14);

        var json = JsonSerializer.Serialize(seat, _options);

        json.Should().Be("14");
        JsonSerializer.Deserialize<SeatNumber>(json, _options).Should().Be(seat);
    }

    [Fact]
    public void HandWrittenStructId_IsHandledByTheFactorysOwnConverter()
    {
        // SectionId has no generated [JsonConverter] attribute, so the factory has to supply one.
        var section = new SectionId("balcony");

        var json = JsonSerializer.Serialize(section, _options);

        json.Should().Be("\"balcony\"");
        JsonSerializer.Deserialize<SectionId>(json, _options).Should().Be(section);
    }

    [Fact]
    public void NullableStructId_RoundTripsAValue()
    {
        CatId? id = CatId.CreateUnique();

        var json = JsonSerializer.Serialize(id, _options);

        json.Should().Be($"\"{id!.Value.Value}\"");
        JsonSerializer.Deserialize<CatId?>(json, _options).Should().Be(id);
    }

    [Fact]
    public void NullableStructId_RoundTripsNull()
    {
        CatId? id = null;

        var json = JsonSerializer.Serialize(id, _options);

        json.Should().Be("null");
        JsonSerializer.Deserialize<CatId?>(json, _options).Should().BeNull();
    }

    [Fact]
    public void StructId_RefusesNull()
    {
        // A struct id has no "absent" value; asking for one is a programming error, not an empty id.
        var act = () => JsonSerializer.Deserialize<CatId>("null", _options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void ClassId_IsWrittenAsItsUnderlyingValue()
    {
        var id = PersonId.CreateUnique();

        var json = JsonSerializer.Serialize(id, _options);

        json.Should().Be($"\"{id.Value}\"");
        JsonSerializer.Deserialize<PersonId>(json, _options).Should().Be(id);
    }

    [Fact]
    public void ClassId_RoundTripsNull()
    {
        PersonId? id = null;

        var json = JsonSerializer.Serialize(id, _options);

        json.Should().Be("null");
        JsonSerializer.Deserialize<PersonId>(json, _options).Should().BeNull();
    }

    [Fact]
    public void ListOfStructIds_RoundTrips()
    {
        List<CatId> ids = [CatId.CreateUnique(), CatId.CreateUnique(), CatId.Empty];

        var json = JsonSerializer.Serialize(ids, _options);

        json.Should().StartWith("[\"").And.Contain(ids[0].Value.ToString());
        JsonSerializer.Deserialize<List<CatId>>(json, _options).Should().Equal(ids);
    }

    [Fact]
    public void DictionaryKeyedByStructId_RoundTrips()
    {
        var first = CatId.CreateUnique();
        var second = CatId.CreateUnique();
        var cats = new Dictionary<CatId, string> { [first] = "Bagheera", [second] = "Shere Khan" };

        var json = JsonSerializer.Serialize(cats, _options);

        json.Should().Contain($"\"{first.Value}\":\"Bagheera\"");
        JsonSerializer.Deserialize<Dictionary<CatId, string>>(json, _options).Should().Equal(cats);
    }

    [Fact]
    public void DictionaryKeyedByPrefixedStructId_UsesTheTextualForm()
    {
        var ticket = TicketId.CreateUnique();
        var seats = new Dictionary<TicketId, SeatNumber> { [ticket] = new(7) };

        var json = JsonSerializer.Serialize(seats, _options);

        json.Should().Be($"{{\"TCK_{ticket.Value}\":7}}");
        JsonSerializer.Deserialize<Dictionary<TicketId, SeatNumber>>(json, _options).Should().Equal(seats);
    }

    [Fact]
    public void DictionaryKeyedByHandWrittenStructId_RoundTrips()
    {
        var sections = new Dictionary<SectionId, int> { [new SectionId("stalls")] = 200 };

        var json = JsonSerializer.Serialize(sections, _options);

        json.Should().Be("{\"stalls\":200}");
        JsonSerializer.Deserialize<Dictionary<SectionId, int>>(json, _options).Should().Equal(sections);
    }

    [Fact]
    public void DtoOfIdsAndValueObjects_RoundTrips()
    {
        var dto = new TicketDto(
            Cat: CatId.CreateUnique(),
            MaybeCat: null,
            Ticket: TicketId.CreateUnique(),
            Seat: new SeatNumber(3),
            Section: new SectionId("stalls"),
            Person: PersonId.CreateUnique(),
            Email: EmailAddress.Create("ticket@example.com"),
            Name: new PersonName("John", "Doe"));

        var json = JsonSerializer.Serialize(dto, _options);

        json.Should().Contain($"\"Cat\":\"{dto.Cat.Value}\"")
            .And.Contain("\"MaybeCat\":null")
            .And.Contain($"\"Ticket\":\"{dto.Ticket.Value}\"")
            .And.Contain("\"Seat\":3")
            .And.Contain("\"Section\":\"stalls\"")
            .And.Contain($"\"Person\":\"{dto.Person.Value}\"")
            .And.Contain("\"Email\":\"ticket@example.com\"");

        var restored = JsonSerializer.Deserialize<TicketDto>(json, _options);

        restored.Should().Be(dto);
    }

    [Fact]
    public void DtoWithAValuedNullableId_RoundTrips()
    {
        var dto = new TicketDto(
            Cat: CatId.CreateUnique(),
            MaybeCat: CatId.CreateUnique(),
            Ticket: TicketId.CreateUnique(),
            Seat: new SeatNumber(3),
            Section: new SectionId("balcony"),
            Person: PersonId.CreateUnique(),
            Email: EmailAddress.Create("ticket@example.com"),
            Name: new PersonName("Jane", "Roe"));

        var restored = JsonSerializer.Deserialize<TicketDto>(JsonSerializer.Serialize(dto, _options), _options);

        restored.Should().Be(dto);
        restored!.MaybeCat.Should().Be(dto.MaybeCat);
    }

    [Fact]
    public void SingleValueObject_IsWrittenAsItsValue()
    {
        var email = EmailAddress.Create("test@example.com");

        JsonSerializer.Serialize(email, _options).Should().Be("\"test@example.com\"");
        JsonSerializer.Deserialize<EmailAddress>("\"test@example.com\"", _options).Should().Be(email);
    }

    [Fact]
    public void ValueObject_KeepsItsShape()
    {
        var name = new PersonName("John", "Ronald", "Doe");

        var restored = JsonSerializer.Deserialize<PersonName>(JsonSerializer.Serialize(name, _options), _options);

        restored.Should().Be(name);
        restored!.MiddleNames.Should().Be("Ronald");
        restored.FullName.Should().Be("John Ronald Doe");
    }
}
