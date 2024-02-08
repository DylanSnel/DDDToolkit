using DDDToolkit.BaseTypes;
using Newtonsoft.Json;
using System.Reflection;

public class SingleValueObjectConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType.IsGenericType &&
               objectType.GetInterfaces().Any(i => i.GetGenericTypeDefinition() == typeof(SingleValueObject<>));
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var valueType = objectType.GetInterfaces().First(i => i.GetGenericTypeDefinition() == typeof(SingleValueObject<>)).GetGenericArguments()[0];
        var value = serializer.Deserialize(reader, valueType);

        // Inside the Read method for both System.Text.Json and Newtonsoft.Json converters
        if (value is null)
        {
            return null!;
        }

        var constructor = objectType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null,
            [valueType],
            null);
        if (constructor == null)
        {
            throw new JsonException($"No suitable constructor found for type {typeof(T).Name}");
        }
        var instance = constructor.Invoke(new object[] { value });
        return instance;
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        var valueType = value.GetType().GetInterfaces().First(i => i.GetGenericTypeDefinition() == typeof(SingleValueObject<>)).GetGenericArguments()[0];
        var propValue = value.GetType().GetProperty("Value")!.GetValue(value);
        serializer.Serialize(writer, propValue, valueType);
    }
}
