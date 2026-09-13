using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Serialization;
using DDDToolkit.Serialization.Converters;
using FluentAssertions;
using System.Text.Json;

namespace DDDToolkit.Tests.Serialization.SystemTextJson;

/// <summary>
/// The factory's contract: it claims exactly the types it can build a converter for. Claiming a type and
/// then throwing when asked for the converter - which is what it used to do for every id that is not a
/// <c>SingleValueObject&lt;T&gt;</c> - turns a serialization into a hard failure at the first request.
/// </summary>
public class SingleValueObjectConverterFactoryTests
{
    private readonly SingleValueObjectConverterFactory _factory = new();
    private readonly JsonSerializerOptions _options = new();

    public static TheoryData<Type> Supported =>
    [
        typeof(CatId),              // generated struct id
        typeof(TicketId),           // generated struct id with a prefix
        typeof(SeatNumber),         // generated struct id over an int
        typeof(SectionId),          // hand written struct id
        typeof(VenueId),            // hand written reference type id, not a SingleValueObject<T>
        typeof(PersonId),           // generated reference type id
        typeof(ValidPersonId),      // its always valid twin
        typeof(EmailAddress),       // single value object
        typeof(ValidEmailAddress),
        typeof(DateOfBirth),        // single value object over a DateOnly
    ];

    public static TheoryData<Type> Unsupported =>
    [
        typeof(PersonName),                 // a value object with more than one value
        typeof(TicketOrder),                // an aggregate root
        typeof(ISingleValueObject),         // the interface itself
        typeof(IEntityId<Guid>),
        typeof(SingleValueObject<string>),  // the abstract base
        typeof(Guid),
        typeof(string),
        typeof(object),
        typeof(CatId?),                     // System.Text.Json wraps the converter for CatId instead
    ];

    [Theory]
    [MemberData(nameof(Supported))]
    public void CanConvert_AcceptsTypesThatWrapASingleValue(Type type) => _factory.CanConvert(type).Should().BeTrue();

    [Theory]
    [MemberData(nameof(Unsupported))]
    public void CanConvert_DeclinesEverythingElse(Type type) => _factory.CanConvert(type).Should().BeFalse();

    [Theory]
    [MemberData(nameof(Supported))]
    public void CreateConverter_NeverThrowsForATypeItAccepted(Type type)
    {
        var act = () => _factory.CreateConverter(type, _options);

        act.Should().NotThrow();
        _factory.CreateConverter(type, _options)!.CanConvert(type).Should().BeTrue();
    }

    [Fact]
    public void CreateConverter_PrefersTheGeneratedConverterOfAStructId()
    {
        // It knows about the prefix, which matters for dictionary keys.
        _factory.CreateConverter(typeof(CatId), _options).Should().BeOfType<CatId.SystemTextJsonConverter>();
    }

    [Fact]
    public void CreateConverter_FallsBackToItsOwnConverterForAHandWrittenStructId()
    {
        _factory.CreateConverter(typeof(SectionId), _options).Should().BeOfType<EntityIdConverter<SectionId, string>>();
    }

    [Fact]
    public void CreateConverter_UsesTheProtectedConstructorOfAReferenceTypeId()
    {
        _factory.CreateConverter(typeof(PersonId), _options).Should().BeOfType<SingleValueObjectConverter<PersonId, Guid>>();
    }

    [Fact]
    public void HandWrittenReferenceTypeId_RoundTripsAndAcceptsNull()
    {
        // Not a SingleValueObject<T>: only the IEntityId<T> interface is there to go on.
        var options = new JsonSerializerOptions().AddDDDToolkitConverters();
        var venue = new VenueId("o2-arena");

        JsonSerializer.Serialize(venue, options).Should().Be("\"o2-arena\"");
        JsonSerializer.Deserialize<VenueId>("\"o2-arena\"", options).Should().Be(venue);
        JsonSerializer.Deserialize<VenueId>("null", options).Should().BeNull();
        JsonSerializer.Serialize<VenueId?>(null, options).Should().Be("null");
    }

    [Fact]
    public void SingleValueObjectOverADateOnly_RoundTrips()
    {
        var options = new JsonSerializerOptions().AddDDDToolkitConverters();
        var date = JsonSerializer.Deserialize<DateOfBirth>("\"1984-04-01\"", options);

        date!.Value.Should().Be(new DateOnly(1984, 4, 1));
        JsonSerializer.Serialize(date, options).Should().Be("\"1984-04-01\"");
    }

    [Fact]
    public void Description_ReportsTheWrappedValueType()
    {
        SingleValueDescription.For(typeof(CatId))!.ValueType.Should().Be<Guid>();
        SingleValueDescription.For(typeof(SeatNumber))!.ValueType.Should().Be<int>();
        SingleValueDescription.For(typeof(EmailAddress))!.ValueType.Should().Be<string>();
        SingleValueDescription.For(typeof(PersonName)).Should().BeNull();
    }

    [Fact]
    public void Description_ReadsAndWritesThroughTheInterface()
    {
        var description = SingleValueDescription.For(typeof(TicketId))!;
        var value = Guid.NewGuid();

        var id = description.Create(value);

        id.Should().Be(new TicketId(value));
        description.GetValue(id).Should().Be(value);
    }

    [Fact]
    public void Description_RethrowsWhatTheConstructorThrew()
    {
        // A twin validates in its constructor; the caller should see that, not a TargetInvocationException.
        var description = SingleValueDescription.For(typeof(ValidEmailAddress))!;

        var act = () => description.Create("not-an-email");

        act.Should().Throw<DDDToolkit.Exceptions.InvalidValueObjectException>();
    }
}
