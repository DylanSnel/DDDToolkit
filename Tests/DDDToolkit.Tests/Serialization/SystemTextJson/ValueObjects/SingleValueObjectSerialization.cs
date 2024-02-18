using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Serialization.Converters;
using System.Text.Json;

namespace DDDToolkit.Tests.Serialization.SystemTextJson.ValueObjects;

public class SingleValueObjectSerialization
{
    private readonly JsonSerializerOptions _options;

    public SingleValueObjectSerialization()
    {
        _options = new JsonSerializerOptions();
        _options.Converters.Add(new SingleValueObjectConverterFactory());
    }

    [Fact]
    public void Serialize_SingleValueObject_ReturnsJsonValue()
    {
        var email = EmailAddress.Create("test@example.com");
        var json = JsonSerializer.Serialize(email, _options);

        Assert.Equal("\"test@example.com\"", json);
    }

    [Fact]
    public void Deserialize_JsonValue_ReturnsSingleValueObject()
    {
        var json = "\"test@example.com\"";
        var email = JsonSerializer.Deserialize<EmailAddress>(json, _options);

        Assert.NotNull(email);
        Assert.Equal("test@example.com", email.Value);
    }

    [Fact]
    public void Deserialize_NullJsonValue_ReturnsNull()
    {
        var json = "null";
        var email = JsonSerializer.Deserialize<EmailAddress?>(json, _options);

        Assert.Null(email);
    }
}
