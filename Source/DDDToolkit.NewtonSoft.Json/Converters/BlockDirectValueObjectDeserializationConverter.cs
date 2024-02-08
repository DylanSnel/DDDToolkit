using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using Newtonsoft.Json;


namespace DDDToolkit.Tests.Serialization.NewtonSoft.ValueObjects;

public class BlockDirectValueObjectDeserializationConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        // Check if objectType is a subclass of ValueObject
        var isValueObject = objectType.IsSubclassOf(typeof(ValueObject));
        var isRaw = typeof(IRaw).IsAssignableFrom(objectType);
        objectType.In
        return isValueObject && !isRaw;
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        throw new JsonSerializationException($"Direct deserialization of {objectType.Name} is not allowed. Please deserialize to Raw<{objectType.Name}> instead.");
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        // Direct serialization of ValueObject descendants can be allowed or handled as needed
        serializer.Serialize(writer, value);
    }

    public override bool CanRead => true;
    public override bool CanWrite => true; // Adjust if you don't want to allow direct serialization
}

