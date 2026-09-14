using DDDToolkit.Serialization;
using Newtonsoft.Json;

namespace DDDToolkit.NewtonSoft.Json.Converters;

/// <summary>
/// Reads and writes every type that wraps a single value - single value objects, reference type ids
/// (<c>[EntityId&lt;T&gt;] partial record</c>) and struct ids
/// (<c>[EntityId&lt;T&gt;] readonly partial record struct</c>) - as the value it wraps, so
/// <c>{ "id": "3fa85f64-..." }</c> rather than <c>{ "id": { "value": "3fa85f64-..." } }</c>.
/// <para>
/// Nullable struct ids (<c>CatId?</c>) are handled here as well, because Newtonsoft.Json asks the
/// converter for the <see cref="Nullable{T}"/> type itself. A JSON <c>null</c> becomes <see langword="null"/>
/// for reference types and for nullable struct ids, and is an error for a struct id that is not nullable -
/// silently substituting the default id would invent an identity that was never written.
/// </para>
/// <para>
/// Dictionary keys are the one place this does not reach: Newtonsoft.Json turns a key into text through the
/// key type's <c>TypeConverter</c> rather than through a <see cref="JsonConverter"/>, so an id used as a
/// dictionary key is written with <c>ToString()</c> (prefix included) and cannot be read back without a
/// <c>TypeConverter</c> of its own. The System.Text.Json integration handles keys in both directions.
/// </para>
/// </summary>
public class SingleValueObjectConverter : JsonConverter
{
    /// <inheritdoc />
    public override bool CanConvert(Type objectType) => Describe(objectType) is not null;

    /// <inheritdoc />
    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(objectType);
        ArgumentNullException.ThrowIfNull(serializer);

        var description = Describe(objectType)
            ?? throw new JsonSerializationException($"'{objectType.Name}' does not wrap a single value.");

        // A struct id is only allowed to be absent when it was declared as TheId?.
        var nullAllowed = !description.IsValueType || Nullable.GetUnderlyingType(objectType) is not null;

        if (reader.TokenType is JsonToken.Null or JsonToken.Undefined)
        {
            return nullAllowed ? null : throw NullNotAllowed(description);
        }

        var value = serializer.Deserialize(reader, description.ValueType);
        if (value is null)
        {
            return nullAllowed ? null : throw NullNotAllowed(description);
        }

        if (!description.CanCreate)
        {
            throw new JsonSerializationException($"No suitable constructor found for type {description.Type.Name}.");
        }

        return description.Create(value);
    }

    /// <inheritdoc />
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(serializer);

        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        // value is never a Nullable<> here: boxing a nullable struct yields the struct itself.
        var description = SingleValueDescription.For(value.GetType())
            ?? throw new JsonSerializationException($"'{value.GetType().Name}' does not wrap a single value.");

        serializer.Serialize(writer, description.GetValue(value));
    }

    private static SingleValueDescription? Describe(Type? objectType)
        => objectType is null ? null : SingleValueDescription.For(Nullable.GetUnderlyingType(objectType) ?? objectType);

    private static JsonSerializationException NullNotAllowed(SingleValueDescription description)
        => new($"Cannot convert null to '{description.Type.Name}'. Declare the member as '{description.Type.Name}?' to allow it.");
}
