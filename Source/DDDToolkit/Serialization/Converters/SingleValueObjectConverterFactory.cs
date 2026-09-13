using DDDToolkit.BaseTypes;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;


namespace DDDToolkit.Serialization.Converters;

public class SingleValueObjectConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeof(ISingleValueObject).IsAssignableFrom(typeToConvert);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        // Walk up the hierarchy: entity ids derive from EntityId<T> which derives from SingleValueObject<T>,
        // and always-valid twins derive from their value object.
        var baseType = typeToConvert.BaseType;
        while (baseType is not null && !(baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(SingleValueObject<>)))
        {
            baseType = baseType.BaseType;
        }

        if (baseType == null)
        {
            throw new InvalidOperationException($"The type {typeToConvert.Name} is not supported by this converter.");
        }

        var valueType = baseType.GetGenericArguments()[0];
        var converterType = typeof(SingleValueObjectConverter<,>).MakeGenericType(typeToConvert, valueType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

// Converter for single value objects
public class SingleValueObjectConverter<TSingleValueObject, TValue> : JsonConverter<TSingleValueObject>
    where TSingleValueObject : SingleValueObject<TValue>
    where TValue : notnull
{
    public override TSingleValueObject? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        TValue value = JsonSerializer.Deserialize<TValue>(ref reader, options)!;
        // Generated value objects have a protected (value) constructor; their always-valid twins a public one.
        var constructorInfo = typeToConvert.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [typeof(TValue)], null);
        if (constructorInfo == null)
        {
            throw new JsonException($"Could not find a constructor for '{typeToConvert.Name}'.");
        }

        return (TSingleValueObject)constructorInfo.Invoke(new object[] { value });
    }

    public override void Write(Utf8JsonWriter writer, TSingleValueObject value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value.Value, options);
    }
}
