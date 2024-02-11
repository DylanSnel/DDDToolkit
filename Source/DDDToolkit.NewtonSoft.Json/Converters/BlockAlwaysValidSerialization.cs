using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using Newtonsoft.Json;

namespace DDDToolkit.Tests.Serialization.NewtonSoft.ValueObjects;

/// <summary>
/// Blocks serialization of <see cref="IAlwaysValid"/> descendants.
/// This is because <see cref="IAlwaysValid"/> descendants are always valid and should not be serialized directly.
/// Deserialization of and IALwaysValid descendant could lead to objects being in an invalid state.
/// </summary>
public class BlockAlwaysValidSerialization : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return typeof(IAlwaysValid).IsAssignableFrom(objectType);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        throw new SerializationNotAllowedException(objectType);
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
    }

    public override bool CanRead => true;
    public override bool CanWrite => false; // Adjust if you don't want to allow direct serialization
}

