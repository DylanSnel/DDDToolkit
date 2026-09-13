using DDDToolkit.Initialization;
using DDDToolkit.Serialization.Converters;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DDDToolkit.Serialization;

/// <summary>Registration of the System.Text.Json integration. (The type name's typo is historical: it is public API.)</summary>
public static class DepencyInjection
{
    /// <summary>
    /// Teaches <see cref="DDDJsonSettings.DefaultSettings"/> about single value objects and strongly typed
    /// ids, so they serialize as the value they wrap, and blocks direct deserialization of always valid
    /// twins. Calling it more than once is harmless: the converters are added at most once.
    /// </summary>
    /// <param name="builder">The DDDToolkit builder.</param>
    /// <param name="jsonOptions">Optional further configuration, applied after the converters are in place.</param>
    /// <example>
    /// <code>
    /// services.AddDDDToolkit(ddd => ddd.AddJsonSerialization());
    /// // and, for ASP.NET Core:
    /// services.ConfigureHttpJsonOptions(o => o.SerializerOptions.AddDDDToolkitConverters());
    /// </code>
    /// </example>
    public static IDDDBuilder AddJsonSerialization(this IDDDBuilder builder, Action<JsonSerializerOptions>? jsonOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var settings = DDDJsonSettings.DefaultSettings;
        settings.AddDDDToolkitConverters();
        jsonOptions?.Invoke(settings);

        return builder;
    }

    /// <summary>
    /// Adds the DDDToolkit converters to any <see cref="JsonSerializerOptions"/> - the ASP.NET Core options,
    /// an HTTP client's, a message bus'. Idempotent: a converter already present is not added again, so this
    /// does not throw on options that were only ever configured through this method.
    /// </summary>
    public static JsonSerializerOptions AddDDDToolkitConverters(this JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Order matters: the block converter must get first refusal on always valid twins, which the
        // factory would otherwise happily deserialize.
        AddOnce<BlockAlwaysValidSerialization>(options);
        AddOnce<SingleValueObjectConverterFactory>(options);

        return options;
    }

    private static void AddOnce<TConverter>(JsonSerializerOptions options)
        where TConverter : JsonConverter, new()
    {
        foreach (var converter in options.Converters)
        {
            if (converter is TConverter)
            {
                return;
            }
        }

        options.Converters.Add(new TConverter());
    }
}

/// <summary>The <see cref="JsonSerializerOptions"/> instance <c>AddJsonSerialization</c> configures.</summary>
public static class DDDJsonSettings
{
    /// <summary>
    /// Shared options configured by <see cref="DepencyInjection.AddJsonSerialization"/>. Replace it before
    /// calling that method if you want to start from your own defaults.
    /// </summary>
    public static JsonSerializerOptions DefaultSettings { get; set; } = new();
}
