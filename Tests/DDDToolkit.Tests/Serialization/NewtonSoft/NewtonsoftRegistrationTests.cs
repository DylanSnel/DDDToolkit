using DDDToolkit.Exceptions;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.NewtonSoft.Json;
using DDDToolkit.NewtonSoft.Json.Converters;
using DDDToolkit.NewtonSoft.Json.Resolver;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace DDDToolkit.Tests.Serialization.NewtonSoft;

/// <summary>
/// The registration entry point. Everything that touches the shared
/// <see cref="DDDNewtonsoftSettings.DefaultSettings"/> or <see cref="JsonConvert.DefaultSettings"/> lives in
/// this one class, so the tests run one after another rather than racing each other over a static. The
/// other classes that serialize through <see cref="JsonConvert"/> share its <see cref="NewtonsoftGlobalState"/>
/// collection, because the global default reaches their calls too.
/// </summary>
[Collection(NewtonsoftGlobalState.Name)]
public class NewtonsoftRegistrationTests
{
    [Fact]
    public void AddNewtonSoftJson_ProducesWorkingSettings()
    {
        DDDNewtonsoftSettings.DefaultSettings = new JsonSerializerSettings();

        new ServiceCollection().AddDDDToolkit(ddd => ddd.AddNewtonSoftJson(registerDefault: false));

        var settings = DDDNewtonsoftSettings.DefaultSettings;
        var id = CatId.CreateUnique();

        settings.ContractResolver.Should().BeOfType<DDDContractResolver>();
        JsonConvert.SerializeObject(id, settings).Should().Be($"\"{id.Value}\"");
        JsonConvert.DeserializeObject<CatId>($"\"{id.Value}\"", settings).Should().Be(id);
        JsonConvert.SerializeObject(EmailAddress.Create("test@example.com"), settings).Should().Be("\"test@example.com\"");

        var act = () => JsonConvert.DeserializeObject<ValidEmailAddress>("\"test@example.com\"", settings);
        act.Should().Throw<SerializationNotAllowedException>();
    }

    [Fact]
    public void AddNewtonSoftJson_IsIdempotent()
    {
        DDDNewtonsoftSettings.DefaultSettings = new JsonSerializerSettings();

        new ServiceCollection().AddDDDToolkit(ddd => ddd.AddNewtonSoftJson(registerDefault: false));
        var afterFirstCall = Fingerprint(DDDNewtonsoftSettings.DefaultSettings);
        var resolver = DDDNewtonsoftSettings.DefaultSettings.ContractResolver;

        new ServiceCollection().AddDDDToolkit(ddd => ddd.AddNewtonSoftJson(registerDefault: false));

        Fingerprint(DDDNewtonsoftSettings.DefaultSettings).Should().Equal(afterFirstCall);
        DDDNewtonsoftSettings.DefaultSettings.ContractResolver.Should().BeSameAs(resolver);
    }

    [Fact]
    public void AddNewtonSoftJson_AppliesTheCallersConfiguration()
    {
        DDDNewtonsoftSettings.DefaultSettings = new JsonSerializerSettings();

        new ServiceCollection().AddDDDToolkit(ddd => ddd.AddNewtonSoftJson(
            settings => settings.NullValueHandling = NullValueHandling.Ignore,
            registerDefault: false));

        DDDNewtonsoftSettings.DefaultSettings.NullValueHandling.Should().Be(NullValueHandling.Ignore);
    }

    [Fact]
    public void AddNewtonSoftJson_RegistersTheSettingsAsTheGlobalDefault()
    {
        var previousDefault = JsonConvert.DefaultSettings;
        DDDNewtonsoftSettings.DefaultSettings = new JsonSerializerSettings();

        try
        {
            new ServiceCollection().AddDDDToolkit(ddd => ddd.AddNewtonSoftJson());

            JsonConvert.DefaultSettings.Should().NotBeNull();
            JsonConvert.DefaultSettings!().Should().BeSameAs(DDDNewtonsoftSettings.DefaultSettings);

            var id = CatId.CreateUnique();
            JsonConvert.SerializeObject(id).Should().Be($"\"{id.Value}\"");
        }
        finally
        {
            JsonConvert.DefaultSettings = previousDefault;
        }
    }

    [Fact]
    public void AddDDDToolkitConverters_RegistersTheBlockConverterFirst()
    {
        // Otherwise the single value converter would deserialize an always valid twin without validating it.
        var settings = new JsonSerializerSettings().AddDDDToolkitConverters();

        Fingerprint(settings).Should().Equal(typeof(BlockAlwaysValidSerialization), typeof(SingleValueObjectConverter));
    }

    [Fact]
    public void AddDDDToolkitConverters_DoesNotDuplicateConvertersTheCallerAlreadyAdded()
    {
        var settings = new JsonSerializerSettings { Converters = { new SingleValueObjectConverter() } };

        settings.AddDDDToolkitConverters().AddDDDToolkitConverters();

        Fingerprint(settings).Should().Equal(typeof(SingleValueObjectConverter), typeof(BlockAlwaysValidSerialization));
    }

    private static List<Type> Fingerprint(JsonSerializerSettings settings)
        => [.. settings.Converters.Select(converter => converter.GetType())];
}
