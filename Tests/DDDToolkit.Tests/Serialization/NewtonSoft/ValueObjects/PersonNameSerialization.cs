using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Exceptions;
using FluentAssertions;
using Newtonsoft.Json;

namespace DDDToolkit.Tests.Serialization.NewtonSoft.ValueObjects;
public partial class PersonNameSerialization
{
    readonly JsonSerializerSettings _settings = new()
    {
        Converters = { new BlockAlwaysValidSerialization() }
    };


    [Fact]
    public void SerializeName()
    {
        var name = new PersonName("John", "Doe");

        var json = JsonConvert.SerializeObject(name);
        Action act = () =>
        {
            var deserialized = JsonConvert.DeserializeObject<PersonName>(json, _settings);
        };

        act.Should().Throw<SerializationNotAllowedException>();
    }


    [Fact]
    public void SerializeRawName()
    {

        var name = new PersonName("John", "Doe");
        var json = JsonConvert.SerializeObject(name);

        var deserialized = JsonConvert.DeserializeObject<PersonName.Raw>(json, _settings);
        PersonName newName = deserialized!;
        Assert.Equal(name, newName);
    }

    [Fact]
    public void SerializeRawInvalidName()
    {
        var json = """
                   {"FirstName":"John","LastName":""}
                   """;

        var deserialized = JsonConvert.DeserializeObject<PersonName.Raw>(json, _settings);

        Assert.False(deserialized?.IsValid);

        Assert.Equal("", deserialized!.LastName);
        Action act = () =>
        {

            PersonName invalidName = deserialized;
        };
        act.Should().Throw<InvalidValueObjectException>();

    }
}
