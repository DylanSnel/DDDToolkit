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
    // Tenancy's refusal of a caller who does not hold a key where it is needed, and the two arguments it names.
    // The UI knows the API by its wire, so they are written out here.
    private const string KeyNotHeld = "tenancy.not-permitted";
    private const string KeyArgument = "Key";
    private const string UnitArgument = "Unit";

    /// <summary>
    /// Whether the refusal is for a key that was needed for the whole tenant. <c>tenancy.not-permitted</c> names
    /// the key and the unit the key was needed at, and no unit when it was needed for the whole tenant. Its text
    /// names only the key, so a seat that holds the key at a unit reads that it lacks it: the page that shows the
    /// refusal says where it was needed.
    /// </summary>
    public bool KeyNeededForTheWholeTenant
        => Code == KeyNotHeld
           && Arguments.ContainsKey(KeyArgument)
           && (!Arguments.TryGetValue(UnitArgument, out var unit) || unit.ValueKind == JsonValueKind.Null);

    /// <summary>A problem with no arguments.</summary>
    public static ApiProblem WithoutArguments(string? code, string? title, string? detail = null)
        => new(code, title, detail, new Dictionary<string, JsonElement>());
}
