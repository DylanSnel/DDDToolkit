using System.Text.Json;

namespace Examples.Tenancy.Ui.Api;

/// <summary>
/// What problem+json says about a refusal: the <c>code</c> to branch on, the <c>title</c> to show, the
/// <c>detail</c> when there is one, and the <c>arguments</c> the code was raised with, such as the missing key.
/// </summary>
/// <param name="Code">The refusal's code, such as <c>projects.not-permitted</c>; <see langword="null"/> when the answer has none, as a 401 has none.</param>
/// <param name="Title">The human text.</param>
/// <param name="Detail">More text, when there is any.</param>
/// <param name="Arguments">The code's arguments, by name.</param>
public sealed record ApiProblem(string? Code, string? Title, string? Detail, IReadOnlyDictionary<string, JsonElement> Arguments)
{
    // Tenancy's refusal of a caller who does not hold a key where it is needed, and the two arguments it names; and
    // the toolkit's refusal of a request none of whose ways was met, whose reasons are each way's own refusal. The UI
    // knows the API by its wire, so they are written out here.
    private const string KeyNotHeld = "tenancy.not-permitted";
    private const string KeyArgument = "Key";
    private const string UnitArgument = "Unit";
    private const string NoWayMet = "access.refused";
    private const string ReasonsArgument = "Reasons";

    /// <summary>
    /// Whether the refusal is for a key that was needed for the whole tenant. <c>tenancy.not-permitted</c> names
    /// the key and the unit the key was needed at, and no unit when it was needed for the whole tenant. Its text
    /// names only the key, so a seat that holds the key at a unit reads that it lacks it: the page that shows the
    /// refusal says where it was needed. A request that may be sent one way or another, a seat renaming itself or any
    /// seat with the key, is refused with <c>access.refused</c>, whose reasons hold that refusal where one way was the key.
    /// </summary>
    public bool KeyNeededAtTenant
        => IsKeyAtTenant(Code, Arguments)
           || (Code == NoWayMet
               && Arguments.TryGetValue(ReasonsArgument, out var reasons)
               && reasons.ValueKind == JsonValueKind.Array
               && reasons.EnumerateArray().Any(static reason => IsKeyAtTenant(Text(reason, "code"), ArgumentsOf(reason))));

    /// <summary>A problem with no arguments.</summary>
    public static ApiProblem WithoutArguments(string? code, string? title, string? detail = null)
        => new(code, title, detail, new Dictionary<string, JsonElement>());

    private static bool IsKeyAtTenant(string? code, IReadOnlyDictionary<string, JsonElement> arguments)
        => code == KeyNotHeld
           && arguments.ContainsKey(KeyArgument)
           && (!arguments.TryGetValue(UnitArgument, out var unit) || unit.ValueKind == JsonValueKind.Null);

    /// <summary>One reason's text property, its name matched without regard to case as the API writes it.</summary>
    private static string? Text(JsonElement reason, string name)
        => Property(reason, name) is { ValueKind: JsonValueKind.String } found ? found.GetString() : null;

    /// <summary>One reason's arguments, by name, read as a refusal's own are.</summary>
    private static Dictionary<string, JsonElement> ArgumentsOf(JsonElement reason)
        => Property(reason, "arguments") is { ValueKind: JsonValueKind.Object } found
            ? found.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

    private static JsonElement? Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }
}
