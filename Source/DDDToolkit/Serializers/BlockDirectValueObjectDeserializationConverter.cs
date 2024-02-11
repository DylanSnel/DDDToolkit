using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DDDToolkit.Serializers;
public class BlockDirectValueObjectDeserializationConverter : JsonConverter<IAlwaysValid>
{
    public override bool CanConvert(Type typeToConvert)
    {
        var isValueObject = typeof(IValueObject).IsAssignableFrom(typeToConvert);
        var alwaysValid = typeof(IAlwaysValid).IsAssignableFrom(typeToConvert);
        return isValueObject && alwaysValid;
    }

    public override IAlwaysValid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new SerializationNotAllowedException(typeToConvert);
    }

    public override void Write(Utf8JsonWriter writer, IAlwaysValid value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}
