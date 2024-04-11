using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Exceptions;
using DDDToolkit.NewtonSoft.Json.Converters;
using DDDToolkit.NewtonSoft.Json.Resolver;
using FluentAssertions;
using Newtonsoft.Json;

namespace DDDToolkit.Tests.Serialization.NewtonSoft.ValueObjects;
public partial class PersonNameSerialization
{
    readonly JsonSerializerSettings _settings = new()
    {
        Converters = { new BlockAlwaysValidSerialization() },
        ContractResolver = new DDDContractResolver()

    };


    [Fact]
    public void SerializeName()
    {
        var name = new PersonName("John", "Doe");

        var json = JsonConvert.SerializeObject(name);
        Action act = () =>
        {
            var deserialized = JsonConvert.DeserializeObject<ValidPersonName>(json, _settings);
        };

        act.Should().Throw<SerializationNotAllowedException>();
    }

    [Fact]
    public void SerializeRawName()
    {
        var name = new PersonName("John", "Doe");
        var json = JsonConvert.SerializeObject(name);

        var deserialized = JsonConvert.DeserializeObject<PersonName>(json, _settings);
        ValidPersonName newName = deserialized!.ToValid();
        Assert.Equal(name, newName);
    }

    [Fact]
    public void SerializeRawInvalidName()
    {
        var json = """
                   {"FirstName":"John","LastName":""}
                   """;

        var deserialized = JsonConvert.DeserializeObject<PersonName>(json, _settings);

        Assert.False(deserialized?.IsValid);

        Assert.Equal("", deserialized!.LastName);
        Action act = () =>
        {

            ValidPersonName invalidName = deserialized.ToValid();
        };
        act.Should().Throw<InvalidValueObjectException>();

    }
}
