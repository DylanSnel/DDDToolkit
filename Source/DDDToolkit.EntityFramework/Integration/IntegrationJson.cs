using System.Text.Json;
using DDDToolkit.Serialization.Converters;

namespace DDDToolkit.EntityFramework.Integration;

/// <summary>
/// The serializer options the outbox and the contract registry start from, in one place so the payload
/// that is written and the payload that is read back cannot drift apart.
/// </summary>
internal static class IntegrationJson
{
    /// <summary>
    /// Case-insensitive, with the toolkit's <see cref="SingleValueObjectConverterFactory"/> so class
    /// ids and single value objects are written as their raw value rather than as an object.
    /// </summary>
    public static JsonSerializerOptions CreateDefault()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new SingleValueObjectConverterFactory());
        return options;
    }
}
