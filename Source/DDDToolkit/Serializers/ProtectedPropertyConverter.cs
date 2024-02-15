//using System.Reflection;
//using System.Text.Json;
//using System.Text.Json.Serialization;

//namespace DDDToolkit.Serializers;
//public class ProtectedPropertyConverter<TBase> : JsonConverter<object> where TBase : class
//{
//    public override bool CanConvert(Type typeToConvert)
//    {
//        // Check if the type inherits from the base class or implements the interface
//        return typeof(TBase).IsAssignableFrom(typeToConvert);
//    }

//    public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
//    {
//        if (reader.TokenType != JsonTokenType.StartObject)
//        {
//            throw new JsonException();
//        }

//        var instance = Activator.CreateInstance(typeToConvert); // Assumes a parameterless constructor is available
//        if (instance == null)
//            throw new JsonException($"Unable to create an instance of {typeToConvert}.");

//        while (reader.Read())
//        {
//            if (reader.TokenType == JsonTokenType.EndObject)
//            {
//                return instance;
//            }

//            var propName = reader.GetString();
//            reader.Read();
//            var prop = typeToConvert.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

//            if (prop != null && (prop.SetMethod?.IsPublic == true || prop.SetMethod?.Is == true))
//            {
//                var value = JsonSerializer.Deserialize(ref reader, prop.PropertyType, options);
//                prop.SetValue(instance, value);
//            }
//            else
//            {
//                JsonSerializer.Deserialize(ref reader, typeof(object), options); // Skip if can't set
//            }
//        }

//        throw new JsonException("Error occurred while reading JSON.");
//    }

//    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
//    {
//        JsonSerializer.Serialize(writer, value, value.GetType(), options);
//    }
//}
