using DDDToolkit.Exceptions;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Serialization;
using DDDToolkit.Serialization.Converters;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace DDDToolkit.Tests.Serialization.SystemTextJson;

/// <summary>
/// The registration entry points. Everything that touches the shared
/// <see cref="DDDJsonSettings.DefaultSettings"/> lives in this one class, so the tests run one after
/// another rather than racing each other over a static.
/// </summary>
public class JsonSerializationRegistrationTests
{
    [Fact]
    public void AddJsonSerialization_ProducesWorkingOptions()
    {
        DDDJsonSettings.DefaultSettings = new JsonSerializerOptions();

        new ServiceCollection().AddDDDToolkit(ddd => ddd.AddJsonSerialization());

        var options = DDDJsonSettings.DefaultSettings;
        var id = CatId.CreateUnique();

        JsonSerializer.Serialize(id, options).Should().Be($"\"{id.Value}\"");
        JsonSerializer.Deserialize<CatId>($"\"{id.Value}\"", options).Should().Be(id);
        JsonSerializer.Serialize(EmailAddress.Create("test@example.com"), options).Should().Be("\"test@example.com\"");

        var act = () => JsonSerializer.Deserialize<ValidEmailAddress>("\"test@example.com\"", options);
        act.Should().Throw<SerializationNotAllowedException>();
    }

    [Fact]
    public void AddJsonSerialization_IsIdempotent()
    {
        DDDJsonSettings.DefaultSettings = new JsonSerializerOptions();

        new ServiceCollection().AddDDDToolkit(ddd => ddd.AddJsonSerialization());
        var afterFirstCall = Fingerprint(DDDJsonSettings.DefaultSettings);

        new ServiceCollection().AddDDDToolkit(ddd => ddd.AddJsonSerialization());

        Fingerprint(DDDJsonSettings.DefaultSettings).Should().Equal(afterFirstCall);
    }

    [Fact]
    public void AddJsonSerialization_AppliesTheCallersConfiguration()
    {
        DDDJsonSettings.DefaultSettings = new JsonSerializerOptions();

        new ServiceCollection().AddDDDToolkit(ddd => ddd.AddJsonSerialization(options => options.WriteIndented = true));

        DDDJsonSettings.DefaultSettings.WriteIndented.Should().BeTrue();
    }

    [Fact]
    public void AddDDDToolkitConverters_RegistersTheBlockConverterFirst()
    {
        // Otherwise the factory would deserialize an always valid twin without validating it.
        var options = new JsonSerializerOptions().AddDDDToolkitConverters();

        Fingerprint(options).Should().Equal(typeof(BlockAlwaysValidSerialization), typeof(SingleValueObjectConverterFactory));
    }

    [Fact]
    public void AddDDDToolkitConverters_DoesNotDuplicateConvertersTheCallerAlreadyAdded()
    {
        var options = new JsonSerializerOptions { Converters = { new SingleValueObjectConverterFactory() } };

        options.AddDDDToolkitConverters().AddDDDToolkitConverters();

        Fingerprint(options).Should().Equal(typeof(SingleValueObjectConverterFactory), typeof(BlockAlwaysValidSerialization));
    }

    [Fact]
    public void AddDDDToolkitConverters_KeepsWorkingOnOptionsThatAreAlreadyInUse()
    {
        // JsonSerializerOptions locks its converter list once it has been used; re-registering must not
        // throw, which is the whole point of the idempotency check.
        var options = new JsonSerializerOptions().AddDDDToolkitConverters();
        JsonSerializer.Serialize(CatId.CreateUnique(), options);

        var act = () => options.AddDDDToolkitConverters();

        act.Should().NotThrow();
    }

    private static List<Type> Fingerprint(JsonSerializerOptions options)
        => [.. options.Converters.Select(converter => converter.GetType())];
}
