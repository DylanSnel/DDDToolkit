using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.NewtonSoft.Json;
using FluentAssertions;
using Newtonsoft.Json;

namespace DDDToolkit.Tests.Serialization.NewtonSoft;

/// <summary>
/// Strongly typed ids through Newtonsoft.Json: an id is written as the value it wraps, comes back as the
/// same id, and behaves the same way the System.Text.Json integration does.
/// </summary>
[Collection(NewtonsoftGlobalState.Name)]
public class EntityIdSerializationTests
{
    private readonly JsonSerializerSettings _settings = new JsonSerializerSettings().AddDDDToolkitConverters();

    [Fact]
    public void StructId_IsWrittenAsItsUnderlyingValue()
    {
        var id = CatId.CreateUnique();

        var json = JsonConvert.SerializeObject(id, _settings);

        json.Should().Be($"\"{id.Value}\"");
    }

    [Fact]
    public void StructId_RoundTrips()
    {
        var id = CatId.CreateUnique();

        var restored = JsonConvert.DeserializeObject<CatId>(JsonConvert.SerializeObject(id, _settings), _settings);

        restored.Should().Be(id);
    }

    [Fact]
    public void PrefixedStructId_WritesTheValueWithoutThePrefix()
    {
        var id = TicketId.CreateUnique();

        var json = JsonConvert.SerializeObject(id, _settings);

        json.Should().Be($"\"{id.Value}\"");
        JsonConvert.DeserializeObject<TicketId>(json, _settings).Should().Be(id);
    }

    [Fact]
    public void IntStructId_IsWrittenAsANumber()
    {
        var seat = new SeatNumber(14);

        var json = JsonConvert.SerializeObject(seat, _settings);

        json.Should().Be("14");
        JsonConvert.DeserializeObject<SeatNumber>(json, _settings).Should().Be(seat);
    }

    [Fact]
    public void HandWrittenStructId_RoundTrips()
    {
        var section = new SectionId("balcony");

        var json = JsonConvert.SerializeObject(section, _settings);

        json.Should().Be("\"balcony\"");
        JsonConvert.DeserializeObject<SectionId>(json, _settings).Should().Be(section);
    }

    [Fact]
    public void HandWrittenReferenceTypeId_RoundTripsAndAcceptsNull()
    {
        var venue = new VenueId("o2-arena");

        JsonConvert.SerializeObject(venue, _settings).Should().Be("\"o2-arena\"");
        JsonConvert.DeserializeObject<VenueId>("\"o2-arena\"", _settings).Should().Be(venue);
        JsonConvert.DeserializeObject<VenueId>("null", _settings).Should().BeNull();
    }

    [Fact]
    public void NullableStructId_RoundTripsAValue()
    {
        CatId? id = CatId.CreateUnique();

        var json = JsonConvert.SerializeObject(id, _settings);

        json.Should().Be($"\"{id!.Value.Value}\"");
        JsonConvert.DeserializeObject<CatId?>(json, _settings).Should().Be(id);
    }

    [Fact]
    public void NullableStructId_RoundTripsNull()
    {
        CatId? id = null;

        var json = JsonConvert.SerializeObject(id, _settings);

        json.Should().Be("null");
        JsonConvert.DeserializeObject<CatId?>("null", _settings).Should().BeNull();
    }

    [Fact]
    public void StructId_RefusesNull()
    {
        var act = () => JsonConvert.DeserializeObject<CatId>("null", _settings);

        act.Should().Throw<JsonSerializationException>();
    }

    [Fact]
    public void ClassId_RoundTrips()
    {
        var id = PersonId.CreateUnique();

        var json = JsonConvert.SerializeObject(id, _settings);

        json.Should().Be($"\"{id.Value}\"");
        JsonConvert.DeserializeObject<PersonId>(json, _settings).Should().Be(id);
    }

    [Fact]
    public void ClassId_RoundTripsNull()
    {
        JsonConvert.SerializeObject(null, typeof(PersonId), _settings).Should().Be("null");
        JsonConvert.DeserializeObject<PersonId>("null", _settings).Should().BeNull();
    }

    [Fact]
    public void SingleValueObject_IsWrittenAsItsValue()
    {
        var email = EmailAddress.Create("test@example.com");

        JsonConvert.SerializeObject(email, _settings).Should().Be("\"test@example.com\"");
        JsonConvert.DeserializeObject<EmailAddress>("\"test@example.com\"", _settings).Should().Be(email);
    }

    [Fact]
    public void ListOfStructIds_RoundTrips()
    {
        List<CatId> ids = [CatId.CreateUnique(), CatId.CreateUnique(), CatId.Empty];

        var json = JsonConvert.SerializeObject(ids, _settings);

        json.Should().Contain(ids[0].Value.ToString());
        JsonConvert.DeserializeObject<List<CatId>>(json, _settings).Should().Equal(ids);
    }

    [Fact]
    public void DictionaryValuedByStructId_RoundTrips()
    {
        var cats = new Dictionary<string, CatId> { ["bagheera"] = CatId.CreateUnique() };

        var json = JsonConvert.SerializeObject(cats, _settings);

        json.Should().Be($"{{\"bagheera\":\"{cats["bagheera"].Value}\"}}");
        JsonConvert.DeserializeObject<Dictionary<string, CatId>>(json, _settings).Should().Equal(cats);
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
            Name: new PersonName("John", "Ronald", "Doe"));

        var json = JsonConvert.SerializeObject(dto, _settings);

        json.Should().Contain($"\"Cat\":\"{dto.Cat.Value}\"")
            .And.Contain("\"MaybeCat\":null")
            .And.Contain($"\"Ticket\":\"{dto.Ticket.Value}\"")
            .And.Contain("\"Seat\":3")
            .And.Contain("\"Section\":\"stalls\"")
            .And.Contain($"\"Person\":\"{dto.Person.Value}\"")
            .And.Contain("\"Email\":\"ticket@example.com\"");

        var restored = JsonConvert.DeserializeObject<TicketDto>(json, _settings);

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

        var restored = JsonConvert.DeserializeObject<TicketDto>(JsonConvert.SerializeObject(dto, _settings), _settings);

        restored.Should().Be(dto);
        restored!.MaybeCat.Should().Be(dto.MaybeCat);
    }
}
