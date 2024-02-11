using DDDToolkit.BaseTypes;
using Newtonsoft.Json;
using System.Reflection;

namespace DDDToolkit.NewtonSoft.Json.Converters;

/// <summary>
/// 
/// </summary>
public class SingleValueObjectConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return typeof(ISingleValueObject).IsAssignableFrom(objectType);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        // Determine the type of T in SingleValueObject<T>
        var baseType = objectType;
        while (baseType != null && !baseType.IsGenericType || baseType!.GetGenericTypeDefinition() != typeof(SingleValueObject<,>))
        {
            baseType = baseType.BaseType;
        }
        if (baseType == null)
        {
            throw new JsonSerializationException($"Type {objectType.Name} does not inherit from SingleValueObject<T>.");
        }

        var valueType = baseType.GetGenericArguments()[0];
        var value = serializer.Deserialize(reader, valueType);

        if (value == null)
        {
            return null!;
        }

        // Find the protected constructor
        var constructor = objectType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                          .FirstOrDefault(c => c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType == valueType);

        if (constructor == null)
        {
            throw new JsonSerializationException($"No suitable constructor found for type {objectType.Name}.");
        }

        // Invoke the protected constructor
        var instance = constructor.Invoke(BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { value }, null);
        return instance;
    }
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        var propValue = value.GetType().GetProperty("Value")?.GetValue(value);
        serializer.Serialize(writer, propValue);
    }

}
