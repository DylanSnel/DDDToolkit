using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Exceptions;
using DDDToolkit.Serializers;
using FluentAssertions;
using System.Text.Json;

namespace DDDToolkit.Tests.Serialization.SystemTextJson.ValueObjects;
public partial class PersonNameSerialization
{
    private readonly JsonSerializerOptions _options = new()
    {
        Converters = { new BlockDirectValueObjectDeserializationConverter() }
    };

    [Fact]
    public void SerializeName()
    {
        var name = new PersonName("John", "Doe");

        var json = JsonSerializer.Serialize(name, _options);
        Action act = () =>
        {
            var deserialized = JsonSerializer.Deserialize<ValidPersonName>(json, _options);
        };

        act.Should().Throw<SerializationNotAllowedException>();
    }

    [Fact]
    public void SerializeRawName()
    {
        var json = """
                   {"FirstName":"John","LastName":"Doe"}
                   """;

        var deserialized = JsonSerializer.Deserialize<PersonName>(json, _options);
        PersonName newName = deserialized!;
        Assert.Equal("Doe", newName.LastName);
    }

    [Fact]
    public void SerializeRawInvalidName()
    {
        var json = """
                   {"FirstName":"John","LastName":""}
                   """;

        var deserialized = JsonSerializer.Deserialize<PersonName>(json, _options);

        Assert.False(deserialized?.IsValid);

        Assert.Equal("", deserialized!.LastName);
        Action act = () =>
        {
            ValidPersonName invalidName = deserialized.ToValid();
        };
        act.Should().Throw<InvalidValueObjectException>();
    }
}
