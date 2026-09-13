using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DDDToolkit.Serialization.Converters;

/// <summary>
/// Refuses to deserialize an always valid twin (<c>ValidEmailAddress</c>, <c>ValidPersonName</c>, ...).
/// Such a type promises that every instance passed validation; a document is not trusted, so it cannot make
/// that promise. Deserialize the raw type instead and call <c>ToValid()</c>, which validates.
/// <para>Writing is allowed - the instance was validated when it was built - and writes the same JSON the
/// raw type would.</para>
/// </summary>
public class BlockAlwaysValidSerialization : JsonConverter<IAlwaysValid>
{
    /// <summary>Options without this converter, so <see cref="Write"/> can hand the value on without recursing.</summary>
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> Passthrough = new();

    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
    {
        var isValueObject = typeof(IValueObject).IsAssignableFrom(typeToConvert);
        var alwaysValid = typeof(IAlwaysValid).IsAssignableFrom(typeToConvert);
        return isValueObject && alwaysValid;
    }

    /// <inheritdoc />
    /// <exception cref="SerializationNotAllowedException">Always.</exception>
    public override IAlwaysValid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new SerializationNotAllowedException(typeToConvert);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, IAlwaysValid value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        // The declared type here is IAlwaysValid, which has no members: serializing it as-is would write
        // "{}". Write the runtime type instead, with options that no longer route back into this converter.
        JsonSerializer.Serialize(writer, value, value.GetType(), WithoutBlocking(options));
    }

    private static JsonSerializerOptions WithoutBlocking(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Passthrough.GetValue(options, static source =>
        {
            var copy = new JsonSerializerOptions(source);
            for (var index = copy.Converters.Count - 1; index >= 0; index--)
            {
                if (copy.Converters[index] is BlockAlwaysValidSerialization)
                {
                    copy.Converters.RemoveAt(index);
                }
            }

            return copy;
        });
    }
}
