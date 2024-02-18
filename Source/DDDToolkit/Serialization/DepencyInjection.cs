using DDDToolkit.Initialization;
using DDDToolkit.Serialization.Converters;
using System.Text.Json;

namespace DDDToolkit.Serialization;
public static class DepencyInjection
{
    public static IDDDBuilder AddJsonSerialization(this IDDDBuilder builder, Action<JsonSerializerOptions>? jsonOptions = null)
    {
        var settings = DDDJsonSettings.DefaultSettings;
        settings.Converters.Add(new BlockAlwaysValidSerialization());
        settings.Converters.Add(new SingleValueObjectConverterFactory());
        jsonOptions?.Invoke(settings);

        return builder;
    }
}

public static class DDDJsonSettings
{
    public static JsonSerializerOptions DefaultSettings { get; set; } = new();
}
