using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using System.Text.Json;

namespace DDDToolkit.Tests.Serialization.SystemTextJson.ValueObjects;
public class PersonNameSerialization
{

    [Fact]
    public void SerializeName()
    {

        var name = new PersonName("John", "Doe");

        var json = JsonSerializer.Serialize(name);

        var deserialized = JsonSerializer.Deserialize<PersonName>(json);

        Assert.Equal(name, deserialized);
    }

    [Fact]
    public void SerializeInvalidName()
    {

        var json = """
                   {"FirstName":"John","LastName":""}
                   """;

        var deserialized = JsonSerializer.Deserialize<PersonName>(json);

        Assert.Equal("", deserialized.LastName);
    }
}
