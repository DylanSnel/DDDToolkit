using System.Collections;
using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Localization;

namespace DDDToolkit.Localization;

/// <summary>
/// The texts a package offered with its registration (<see cref="FailureTexts"/>), as a source the localizer
/// asks: the package's resx, read straight from the package's assembly and its satellites.
/// <para>
/// It does not go through the application's <c>IStringLocalizerFactory</c>. That factory finds a resx by the
/// folder the application keeps its own resources in, which says nothing of where a package keeps its, and an
/// application that added no resx of its own may not have registered one at all. A package's resx is named
/// after its marker type, and that is all that is needed to find it.
/// </para>
/// <para>
/// It answers as a resx-backed localizer does: a name in the language of the current UI culture, falling back
/// through its parents to the neutral resx, and, for a check of the translations, what it holds at one
/// language level alone.
/// </para>
/// </summary>
/// <param name="resourceSource">The marker type the resx is named after.</param>
internal sealed class OfferedTexts(Type resourceSource) : IStringLocalizer
{
    private readonly ResourceManager _resources = new(resourceSource);

    /// <summary>
    /// What each offer is asked as, in the order the offers were made: its resx under the codes it was offered
    /// for, by the name a report calls it by. A resx offered for several sets of codes, once for each resource of
    /// a package say, is read through one reader.
    /// </summary>
    public static IEnumerable<NamedSource> SourcesOf(IEnumerable<FailureTexts> offers)
    {
        var readers = new Dictionary<Type, OfferedTexts>();
        foreach (var offered in offers)
        {
            if (!readers.TryGetValue(offered.ResourceSource, out var texts))
            {
                texts = new OfferedTexts(offered.ResourceSource);
                readers.Add(offered.ResourceSource, texts);
            }

            yield return new NamedSource(
                offered.ResourceSource.Name + ".resx",
                offered.Entries is { } entries ? new RenamedLocalizer(texts, entries) : texts);
        }
    }

    /// <inheritdoc />
    public LocalizedString this[string name]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(name);

            var found = _resources.GetString(name, CultureInfo.CurrentUICulture);
            return new LocalizedString(name, found ?? name, resourceNotFound: found is null, searchedLocation: resourceSource.FullName);
        }
    }

    /// <inheritdoc />
    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var found = this[name];
            return found.ResourceNotFound
                ? found
                : new LocalizedString(name, string.Format(CultureInfo.CurrentCulture, found.Value, arguments), resourceNotFound: false, searchedLocation: found.SearchedLocation);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// What the resx holds for the current UI culture itself, and with <paramref name="includeParentCultures"/>
    /// for its parents down to the neutral resx as well, each name once. A language the package ships no resx
    /// for holds nothing.
    /// </remarks>
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var level = CultureInfo.CurrentUICulture; ; level = level.Parent)
        {
            if (_resources.GetResourceSet(level, createIfNotExists: true, tryParents: false) is { } held)
            {
                foreach (DictionaryEntry entry in held)
                {
                    if (entry is { Key: string name, Value: string text } && seen.Add(name))
                    {
                        yield return new LocalizedString(name, text, resourceNotFound: false, searchedLocation: resourceSource.FullName);
                    }
                }
            }

            if (!includeParentCultures || Equals(level, CultureInfo.InvariantCulture))
            {
                yield break;
            }
        }
    }
}
