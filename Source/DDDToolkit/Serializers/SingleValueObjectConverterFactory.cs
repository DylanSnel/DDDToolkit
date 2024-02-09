using DDDToolkit.BaseTypes;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;



// Factory to create converters for single value objects
public class SingleValueObjectConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeof(ISingleValueObject).IsAssignableFrom(typeToConvert);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var baseType = typeToConvert.BaseType;
        if (baseType == null || !baseType.IsGenericType || baseType.GetGenericTypeDefinition() != typeof(SingleValueObject<>))
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
        var constructorInfo = typeToConvert.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(TValue) }, null);
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
