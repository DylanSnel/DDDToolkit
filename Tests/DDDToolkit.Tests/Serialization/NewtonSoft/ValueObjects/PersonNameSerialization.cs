using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using Newtonsoft.Json;

namespace DDDToolkit.Tests.Serialization.NewtonSoft.ValueObjects;
public partial class PersonNameSerialization
{
    readonly JsonSerializerSettings _settings = new()
    {
        Converters = { new BlockDirectValueObjectDeserializationConverter() }
    };
    [Fact]
    public void SerializeName()
    {
        var name = new PersonName("John", "Doe");

        var json = JsonConvert.SerializeObject(name);

        var deserialized = JsonConvert.DeserializeObject<PersonName>(json, _settings);

        Assert.Equal(name, deserialized);
    }

    [Fact]
    public void SerializeInvalidName()
    {

        var json = """
                   {"FirstName":"John","LastName":""}
                   """;

        var deserialized = JsonConvert.DeserializeObject<PersonName>(json, _settings);

        Assert.Equal("", deserialized.LastName);
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
                   {"FirstName":"John","LastName":null}
                   """;

        var deserialized = JsonConvert.DeserializeObject<PersonName.Raw>(json, _settings);

        Assert.False(deserialized?.IsValid);
    }
}
