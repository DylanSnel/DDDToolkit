using DDDToolkit.BaseTypes;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

public class SingleValueObjectConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        // Check if the type is a generic type and implements SingleValueObject<>
        return typeToConvert.IsGenericType &&
               typeToConvert.GetInterfaces().Any(i => i.GetGenericTypeDefinition() == typeof(SingleValueObject<>));
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var genericType = typeToConvert.GetInterfaces().First(i => i.GetGenericTypeDefinition() == typeof(SingleValueObject<>));
        var valueType = genericType.GetGenericArguments()[0]; // Get the T in SingleValueObject<T>
        var converterType = typeof(SingleValueObjectConverter<,>).MakeGenericType(typeToConvert, valueType);
        return (JsonConverter)Activator.CreateInstance(converterType, BindingFlags.Instance | BindingFlags.Public, null, new object[] { }, null);
    }

    private class SingleValueObjectConverter<T, TValue> : JsonConverter<T> where T : SingleValueObject<TValue>
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            TValue value = JsonSerializer.Deserialize<TValue>(ref reader, options);
            // Inside the Read method for both System.Text.Json and Newtonsoft.Json converters
            if (value is default(T))
            {
                return default!;
            }

            var constructor = typeof(T).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                [typeof(TValue)],
                null);
            if (constructor == null)
            {
                throw new JsonException($"No suitable constructor found for type {typeof(T).Name}");
            }
            var instance = (T)constructor.Invoke(new object[] { value });
            return instance;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value.Value, options);
        }
    }
}
