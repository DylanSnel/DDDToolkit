using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace DDDToolkit.Messaging.Postgres;

/// <summary>
/// Reads the options of the sink and the consumer from a configuration section, by hand rather than with
/// the configuration binder. That way a key the options do not know fails by name instead of being
/// ignored, a value that does not parse says which key it was in, and the sink's routing
/// (<c>Queue</c>, <c>Queues</c>, <c>Topics</c>), which are methods rather than properties, can be
/// configured too.
/// </summary>
internal static class PgmqSettings
{
    /// <summary>
    /// Hands every key in <paramref name="configuration"/> to its reader in <paramref name="readers"/>,
    /// matched without regard to case as configuration keys are, and fails on a key it has no reader for.
    /// </summary>
    public static void Read<TOptions>(
        IConfiguration configuration,
        TOptions options,
        IReadOnlyDictionary<string, Action<TOptions, IConfigurationSection>> readers)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        foreach (var setting in configuration.GetChildren())
        {
            if (!readers.TryGetValue(setting.Key, out var read))
            {
                throw new InvalidOperationException(
                    $"'{setting.Path}' is not a setting of {typeof(TOptions).Name}. " +
                    $"The settings it reads are {string.Join(", ", readers.Keys)}.");
            }

            read(options, setting);
        }
    }

    /// <summary>A time span, written as configuration writes them: <c>00:00:05</c> for five seconds.</summary>
    public static TimeSpan TimeSpan(IConfigurationSection setting)
        => System.TimeSpan.TryParse(setting.Value, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw Invalid(setting, "a time span such as 00:00:05");

    /// <summary>A whole number.</summary>
    public static int Int32(IConfigurationSection setting)
        => int.TryParse(setting.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw Invalid(setting, "a whole number");

    /// <summary><c>true</c> or <c>false</c>.</summary>
    public static bool Boolean(IConfigurationSection setting)
        => bool.TryParse(setting.Value, out var value)
            ? value
            : throw Invalid(setting, "true or false");

    /// <summary>A queue name: text, not empty.</summary>
    public static string Name(IConfigurationSection setting)
        => string.IsNullOrWhiteSpace(setting.Value)
            ? throw Invalid(setting, "a queue name")
            : setting.Value;

    /// <summary>A list of queue names, as an array in JSON or <c>Key:0</c>, <c>Key:1</c> elsewhere.</summary>
    public static IReadOnlyList<string> Names(IConfigurationSection setting)
    {
        var names = setting.GetChildren().Select(Name).ToArray();

        return names.Length == 0 ? throw Invalid(setting, "a list of queue names") : names;
    }

    private static InvalidOperationException Invalid(IConfigurationSection setting, string expected)
        => new($"'{setting.Path}' is '{setting.Value}', which is not {expected}.");
}
