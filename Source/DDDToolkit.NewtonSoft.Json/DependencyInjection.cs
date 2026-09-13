using DDDToolkit.Initialization;
using DDDToolkit.NewtonSoft.Json.Converters;
using DDDToolkit.NewtonSoft.Json.Resolver;
using Newtonsoft.Json;

namespace DDDToolkit.NewtonSoft.Json;

/// <summary>Registration of the Newtonsoft.Json integration.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Teaches <see cref="DDDNewtonsoftSettings.DefaultSettings"/> about single value objects and strongly
    /// typed ids, so they serialize as the value they wrap, hides <c>[Internal]</c> members and blocks
    /// direct deserialization of always valid twins. Calling it more than once is harmless: nothing is
    /// registered twice.
    /// </summary>
    /// <param name="builder">The DDDToolkit builder.</param>
    /// <param name="jsonOptions">Optional further configuration, applied after the converters are in place.</param>
    /// <param name="registerDefault">
    /// When true (the default) the settings also become <see cref="JsonConvert.DefaultSettings"/>, so plain
    /// <c>JsonConvert.SerializeObject(x)</c> calls pick them up.
    /// </param>
    /// <example>
    /// <code>
    /// services.AddDDDToolkit(ddd => ddd.AddNewtonSoftJson());
    /// // and, for ASP.NET Core:
    /// services.AddControllers().AddNewtonsoftJson(o => o.SerializerSettings.AddDDDToolkitConverters());
    /// </code>
    /// </example>
    public static IDDDBuilder AddNewtonSoftJson(this IDDDBuilder builder, Action<JsonSerializerSettings>? jsonOptions = null, bool registerDefault = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var settings = DDDNewtonsoftSettings.DefaultSettings;
        settings.AddDDDToolkitConverters();
        jsonOptions?.Invoke(settings);

        if (registerDefault)
        {
            JsonConvert.DefaultSettings = () => settings;
        }

        return builder;
    }

    /// <summary>
    /// Adds the DDDToolkit contract resolver and converters to any <see cref="JsonSerializerSettings"/>.
    /// Idempotent: converters already present are not added again, and a contract resolver that is already
    /// a <see cref="DDDContractResolver"/> is left in place.
    /// </summary>
    public static JsonSerializerSettings AddDDDToolkitConverters(this JsonSerializerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.ContractResolver is not DDDContractResolver)
        {
            settings.ContractResolver = new DDDContractResolver();
        }

        // Order matters: the block converter must get first refusal on always valid twins, which the
        // single value converter would otherwise happily deserialize.
        AddOnce<BlockAlwaysValidSerialization>(settings);
        AddOnce<SingleValueObjectConverter>(settings);

        return settings;
    }

    private static void AddOnce<TConverter>(JsonSerializerSettings settings)
        where TConverter : JsonConverter, new()
    {
        foreach (var converter in settings.Converters)
        {
            if (converter is TConverter)
            {
                return;
            }
        }

        settings.Converters.Add(new TConverter());
    }
}

/// <summary>The <see cref="JsonSerializerSettings"/> instance <c>AddNewtonSoftJson</c> configures.</summary>
public static class DDDNewtonsoftSettings
{
    /// <summary>
    /// Shared settings configured by <see cref="DependencyInjection.AddNewtonSoftJson"/>. Replace it before
    /// calling that method if you want to start from your own defaults.
    /// </summary>
    public static JsonSerializerSettings DefaultSettings { get; set; } = new();
}
