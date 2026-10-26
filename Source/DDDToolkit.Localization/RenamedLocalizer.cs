using Microsoft.Extensions.Localization;

namespace DDDToolkit.Localization;

/// <summary>
/// A source of translations read under other names than its entries have: for each code a failure carries,
/// the entry of the source that holds its text. It answers the codes it was given and nothing else, and it
/// answers as the source does, language by language, so a check of the translations sees through it.
/// </summary>
/// <param name="inner">The source that holds the texts, usually the localizer of a package's resx.</param>
/// <param name="entries">For each code a failure carries, the name of the entry that holds its text.</param>
internal sealed class RenamedLocalizer(IStringLocalizer inner, IReadOnlyDictionary<string, string> entries) : IStringLocalizer
{
    /// <summary>The codes that read each entry: several codes may read one.</summary>
    private readonly ILookup<string, string> _codes = entries.ToLookup(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    /// <inheritdoc />
    public LocalizedString this[string name]
        => entries.TryGetValue(name, out var entry)
            ? Under(name, inner[entry])
            : new LocalizedString(name, name, resourceNotFound: true);

    /// <inheritdoc />
    public LocalizedString this[string name, params object[] arguments]
        => entries.TryGetValue(name, out var entry)
            ? Under(name, inner[entry, arguments])
            : new LocalizedString(name, name, resourceNotFound: true);

    /// <inheritdoc />
    /// <remarks>What the source holds at this level, each entry once for every code that reads it. An entry no code reads is left out.</remarks>
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        => inner.GetAllStrings(includeParentCultures)
            .SelectMany(found => _codes[found.Name].Select(code => Under(code, found)));

    /// <summary>What the source found for an entry, under the code that asked.</summary>
    private static LocalizedString Under(string code, LocalizedString found)
        => new(code, found.ResourceNotFound ? code : found.Value, found.ResourceNotFound, found.SearchedLocation);
}
