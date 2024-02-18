using DDDToolkit.Initialization;
using DDDToolkit.NewtonSoft.Json.Converters;
using DDDToolkit.NewtonSoft.Json.Resolver;
using Newtonsoft.Json;

namespace DDDToolkit.NewtonSoft.Json;
public static class DependencyInjection
{
    public static IDDDBuilder AddNewtonSoftJson(this IDDDBuilder builder, Action<JsonSerializerSettings>? jsonOptions = null, bool registerDefault = true)
    {
        var settings = DDDNewtonsoftSettings.DefaultSettings;
        settings.ContractResolver = new DDDContractResolver();
        settings.Converters.Add(new BlockAlwaysValidSerialization());
        settings.Converters.Add(new SingleValueObjectConverter());
        jsonOptions?.Invoke(settings);

        if (registerDefault)
        {
            JsonConvert.DefaultSettings = () => settings;
        }

        return builder;
    }
}


public static class DDDNewtonsoftSettings
{
    public static JsonSerializerSettings DefaultSettings { get; set; } = new();
}

