using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Exceptions;
using Newtonsoft.Json;


namespace DDDToolkit.Tests.Serialization.NewtonSoft.ValueObjects;

public class BlockDirectValueObjectDeserializationConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        // Check if objectType is a subclass of ValueObject
        var isValueObject = objectType.IsSubclassOf(typeof(ValueObject));
        var isRaw = typeof(IRaw).IsAssignableFrom(objectType);
        var alwaysValid = typeof(IAlwaysValid).IsAssignableFrom(objectType);
        return isValueObject && alwaysValid && !isRaw;
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        throw new SerializationNotAllowedException(objectType);
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        // Direct serialization of ValueObject descendants can be allowed or handled as needed
        serializer.Serialize(writer, value);
    }

    public override bool CanRead => true;
    public override bool CanWrite => true; // Adjust if you don't want to allow direct serialization
}

