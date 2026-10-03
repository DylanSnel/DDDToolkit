namespace DDDToolkit.Localization;

/// <summary>
/// The texts a package ships for the failures it reports, as the package's own registration offers them: the
/// resx named after a marker type of the package, and, where the application chooses the codes its failures
/// carry, which code reads which entry.
/// <para>
/// A package knows which of its texts go with what it registers, and an application that had to add them
/// itself would have to know that too, for every package and again for every thing it registers. So the
/// registration offers them (<see cref="FailureTextsServiceCollectionExtensions.AddFailureTexts{TResource}"/>),
/// and the application writes no line for them.
/// </para>
/// <para>
/// It is an offer, and changes nothing by itself. Only an application that phrases its failures in the
/// reader's language reads it: <c>AddDDDToolkitLocalization</c> of <c>DDDToolkit.Localization</c> asks the
/// application's own sources first, wherever they were added, then every offer in the order it was made, and
/// the toolkit's own messages last. So a text of the application's own for a package's code is the one a
/// reader gets, and an application that registers no localizer gets the domain's own messages, as before.
/// </para>
/// <para>
/// The resx is read from the package's own assembly, by the marker type's full name, and its satellite
/// assemblies. It does not go through the application's <c>IStringLocalizerFactory</c>, so it is found
/// whatever folder the application keeps its own resources in, and without <c>AddLocalization()</c>.
/// </para>
/// </summary>
public sealed class FailureTexts
{
    /// <summary>A package's texts, read under the codes of <paramref name="entries"/>, or under the names of the entries themselves.</summary>
    /// <param name="resourceSource">The marker type the resx is named after, in the assembly that holds it.</param>
    /// <param name="entries">
    /// For each code a failure carries, the name of the entry that holds its text, or <see langword="null"/>
    /// when the failures carry the entries' own names. Only the codes listed are answered. Several codes may
    /// read one entry, and the same resx may be offered more than once, each time for other codes.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="resourceSource"/> is null.</exception>
    /// <exception cref="ArgumentException">A code or an entry's name in <paramref name="entries"/> is empty.</exception>
    public FailureTexts(Type resourceSource, IReadOnlyDictionary<string, string>? entries = null)
    {
        ArgumentNullException.ThrowIfNull(resourceSource);

        ResourceSource = resourceSource;
        if (entries is null)
        {
            return;
        }

        // Copied, so the codes an offer answers are the ones it was made with, whatever happens to the caller's map.
        var renamed = new Dictionary<string, string>(entries.Count, StringComparer.Ordinal);
        foreach (var (code, entry) in entries)
        {
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(entry))
            {
                throw new ArgumentException("A code and the entry that holds its text both have a name.", nameof(entries));
            }

            renamed[code] = entry;
        }

        Entries = renamed;
    }

    /// <summary>The marker type the resx is named after.</summary>
    public Type ResourceSource { get; }

    /// <summary>
    /// For each code a failure carries, the name of the entry that holds its text, or <see langword="null"/>
    /// when the failures carry the entries' own names.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Entries { get; }
}
