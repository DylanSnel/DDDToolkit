using DDDToolkit.Abstractions.Validation;
using DDDToolkit.BaseTypes;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using Newtonsoft.Json;


namespace DDDToolkit.Tests.Serialization.NewtonSoft.ValueObjects;
public class PersonNameSerialization
{
    readonly JsonSerializerSettings _settings = new()
    {
        Converters = { new RawJsonConverter(), new BlockDirectValueObjectDeserializationConverter() }
    };
    [Fact]
    public void SerializeName()
    {
        var name = new PersonName("John", "Doe");

        var json = JsonConvert.SerializeObject(name);

        var deserialized = JsonConvert.DeserializeObject<PersonName>(json, _settings);

        Assert.Equal(name, deserialized);
    }

    [Fact]
    public void SerializeInvalidName()
    {

        var json = """
                   {"FirstName":"John","LastName":""}
                   """;

        var deserialized = JsonConvert.DeserializeObject<PersonName>(json, _settings);

        Assert.Equal("", deserialized.LastName);
    }

    [Fact]
    public void SerializeRawName()
    {

        var name = new PersonName("John", "Doe");

        var json = JsonConvert.SerializeObject(name);

        var deserialized = JsonConvert.DeserializeObject<Raw<PersonName>>(json, _settings);
        PersonName newName = deserialized!;
        Assert.Equal(name, newName);
    }

    [Fact]
    public void SerializeRawInvalidName()
    {
        var settings = new JsonSerializerSettings
        {
            Converters = { new RawJsonConverter() }
        };
        var json = """
                   {"FirstName":"John","LastName":null}
                   """;

        var deserialized = JsonConvert.DeserializeObject<Raw<PersonName>>(json, _settings);

        Assert.False(deserialized?.IsValid);
    }


    public class RawJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            // Check if the type is a Raw<T> by looking for the Raw<> open generic type.
            if (!objectType.IsGenericType)
                return false;
            return objectType.GetGenericTypeDefinition() == typeof(Raw<>);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            // Get the TValueObject type argument from the Raw<T> type.
            var valueType = objectType.GetGenericArguments()[0];

            // Temporarily remove the blocking converter to avoid throwing exceptions.
            var converters = serializer.Converters.ToList();
            var blockConverter = converters.OfType<BlockDirectValueObjectDeserializationConverter>().FirstOrDefault();
            if (blockConverter != null)
            {
                serializer.Converters.Remove(blockConverter);
            }

            // Deserialize the JSON into the TValueObject type.
            var valueObject = serializer.Deserialize(reader, valueType);

            // Re-add the blocking converter after deserialization is done.
            if (blockConverter != null)
            {
                serializer.Converters.Add(blockConverter);
            }

            // Create an instance of Raw<TValueObject> using reflection.
            var rawType = typeof(Raw<>).MakeGenericType(valueType);
            return Activator.CreateInstance(rawType, new[] { valueObject });
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            // Extract the TValueObject from Raw<T> and serialize it.
            // Use reflection to get the Value property from Raw<T>.
            var valueProperty = value.GetType().GetField("_valueObject");
            var valueObject = valueProperty.GetValue(value);

            serializer.Serialize(writer, valueObject);
        }
    }

    public class BlockDirectValueObjectDeserializationConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            // Check if objectType is a subclass of ValueObject
            return typeof(ValueObject).IsAssignableFrom(objectType);
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
}
