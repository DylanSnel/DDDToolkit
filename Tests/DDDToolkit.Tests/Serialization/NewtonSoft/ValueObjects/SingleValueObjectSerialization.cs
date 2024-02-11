using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.NewtonSoft.Json.Converters;
using Newtonsoft.Json;

namespace DDDToolkit.Tests.Serialization.NewtonSoft.ValueObjects;

public class NewtonsoftJsonSingleValueObjectConverterTests
{
    private readonly JsonSerializerSettings _settings;

    public NewtonsoftJsonSingleValueObjectConverterTests()
    {
        _settings = new JsonSerializerSettings();
        _settings.Converters.Add(new SingleValueObjectConverter()); // Assuming this is your converter
    }

    [Fact]
    public void Serialize_SingleValueObject_ReturnsJsonValue()
    {
        var email = EmailAddress.Create("test@example.com");
        var json = JsonConvert.SerializeObject(email, _settings);

        Assert.Equal("\"test@example.com\"", json);
    }

    [Fact]
    public void Deserialize_JsonValue_ReturnsSingleValueObject()
    {
        var json = "\"test@example.com\"";
        var email = JsonConvert.DeserializeObject<EmailAddress>(json, _settings);

        Assert.NotNull(email);
        Assert.Equal("test@example.com", email.Value);
    }

    [Fact]
    public void Deserialize_NullJsonValue_ReturnsNull()
    {
        var json = "null";
        var email = JsonConvert.DeserializeObject<EmailAddress?>(json, _settings);

        Assert.Null(email);
    }
}
