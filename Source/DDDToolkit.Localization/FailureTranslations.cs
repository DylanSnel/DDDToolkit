using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using DDDToolkit.Invariants;
using Microsoft.Extensions.Localization;

namespace DDDToolkit.Localization;

/// <summary>
/// Checks that every failure you expect is translated in every language you support, so a missing
/// translation fails a test instead of showing up in front of a user:
/// <code>
/// [Fact]
/// public void Every_failure_is_translated_in_every_language()
///     =&gt; FailureTranslations
///         .Check(localizer, "en", "nl", "de")      // the first is the language of your neutral resx
///         .Invariants(typeof(Order).Assembly)       // every IInvariant, found and asked for its code
///         .Codes("Street.TooLong", "City.Required") // codes your value objects report
///         .ToolkitCodes()                           // the toolkit's own messages
///         .Verify();
/// </code>
/// <para>
/// It asks the sources the way <see cref="FailureLocalizer"/> does at run time, one language level at a
/// time (<c>nl-NL</c>, then <c>nl</c>, then your neutral resx), and reports three things:
/// </para>
/// <list type="bullet">
/// <item><see cref="FailureTranslationProblem.Missing"/>: nobody knows the key.</item>
/// <item><see cref="FailureTranslationProblem.FallsBack"/>: nothing translates it into this language, so readers get another one.</item>
/// <item>
/// <see cref="FailureTranslationProblem.Hidden"/>: the toolkit translates it, but a key in your neutral
/// resx wins first. This is the one that is easy to miss: overriding a toolkit message in
/// <c>SharedFailures.resx</c> alone replaces its Dutch text with your English one.
/// </item>
/// </list>
/// <para>
/// It reads what each source holds per language with <c>GetAllStrings(includeParentCultures: false)</c>,
/// which the resx-backed <c>IStringLocalizer</c> supports; a localizer of your own is checked as far as
/// its <c>GetAllStrings</c> is honest about the current UI culture.
/// </para>
/// <para>
/// Nothing here depends on a test framework: <see cref="Verify"/> throws, and <see cref="Findings"/>
/// returns the list. It is as much at home in a development-only check at startup as in a test.
/// </para>
/// </summary>
public sealed class FailureTranslations
{
    private readonly FailureLocalizer _localizer;
    private readonly CultureInfo[] _cultures;
    private readonly List<Expected> _expected = [];
    private readonly Dictionary<(NamedSource Source, string Culture), HashSet<string>> _held = [];

    private FailureTranslations(FailureLocalizer localizer, CultureInfo[] cultures)
    {
        _localizer = localizer;
        _cultures = cultures;
    }

    /// <summary>Starts a check of <paramref name="localizer"/> in <paramref name="cultures"/>.</summary>
    /// <param name="localizer">
    /// The localizer the application uses, as registered by <c>AddDDDToolkitLocalization()</c>. It must be
    /// a <see cref="FailureLocalizer"/>, because the check needs to see its sources.
    /// </param>
    /// <param name="cultures">
    /// Every language you support. The first is the language your neutral resx files are written in, the
    /// ones without a culture in their name, because that is the one language they count for.
    /// </param>
    public static FailureTranslations Check(IFailureLocalizer localizer, params IEnumerable<string> cultures)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(cultures);

        if (localizer is not FailureLocalizer failureLocalizer)
        {
            throw new ArgumentException(
                "Only a FailureLocalizer can be checked, because the check reads its sources. This is a "
                + localizer.GetType().Name + ".",
                nameof(localizer));
        }

        var parsed = cultures.Select(CultureInfo.GetCultureInfo).ToArray();
        if (parsed.Length == 0)
        {
            throw new ArgumentException("Name at least one culture: the language of your neutral resx.", nameof(cultures));
        }

        return new FailureTranslations(failureLocalizer, parsed);
    }

    /// <summary>Expects every code in <paramref name="codes"/> to be translated, such as the codes your value objects report.</summary>
    /// <param name="codes">The failure codes, exactly as the failures carry them.</param>
    public FailureTranslations Codes(params IEnumerable<string> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);

        foreach (var code in codes)
        {
            ArgumentException.ThrowIfNullOrEmpty(code, nameof(codes));
            _expected.Add(new Expected([code]));
        }

        return this;
    }

    /// <summary>
    /// Expects the code of every <see cref="IInvariant{TEntity}"/> in <paramref name="assemblies"/> to be
    /// translated, as <c>{EntityType}.{Code}</c> or as the bare code, the two keys the localizer tries.
    /// Each rule is created with its parameterless constructor to read its code, exactly as the generated
    /// code does.
    /// </summary>
    /// <param name="assemblies">The assemblies holding your entities.</param>
    public FailureTranslations Invariants(params IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var assembly in assemblies)
        {
            foreach (var type in LoadableTypes(assembly))
            {
                if (type is { IsAbstract: false, IsGenericTypeDefinition: false }
                    && InvariantInterface(type) is { } @interface
                    && type.GetConstructor(Type.EmptyTypes) is not null)
                {
                    var rule = Activator.CreateInstance(type);
                    var code = (string?)@interface.GetProperty(nameof(IInvariant<object>.Code))!.GetValue(rule);
                    if (string.IsNullOrEmpty(code))
                    {
                        continue;
                    }

                    // The rule is nested in the entity it is about, and that entity is the one that reports it.
                    var entity = type.DeclaringType ?? @interface.GetGenericArguments()[0];
                    _expected.Add(new Expected([entity.Name + "." + code, code]));
                }
            }
        }

        return this;
    }

    /// <summary>
    /// Expects the toolkit's own messages to be translated in every language you support. They ship in
    /// English and Dutch; for any other language add their keys to your resx.
    /// </summary>
    public FailureTranslations ToolkitCodes()
    {
        var neutral = FailureLocalizer.ToolkitResources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);
        foreach (DictionaryEntry entry in neutral!)
        {
            _expected.Add(new Expected([(string)entry.Key]));
        }

        return this;
    }

    /// <summary>Everything the check found, in culture order and then key order. Empty when all is well.</summary>
    public IReadOnlyList<FailureTranslationFinding> Findings()
    {
        var findings = new List<FailureTranslationFinding>();

        foreach (var culture in _cultures)
        {
            foreach (var expected in _expected.DistinctBy(expected => string.Join("|", expected.Keys)).OrderBy(expected => expected.Keys[^1], StringComparer.Ordinal))
            {
                if (Inspect(culture, expected) is { } finding)
                {
                    findings.Add(finding);
                }
            }
        }

        return findings;
    }

    /// <summary>Throws <see cref="MissingFailureTranslationsException"/>, listing every finding, unless there are none.</summary>
    public void Verify()
    {
        var findings = Findings();
        if (findings.Count > 0)
        {
            throw new MissingFailureTranslationsException(findings);
        }
    }

    // ---------------------------------------------------------------- the lookup, level by level

    /// <summary>Follows the keys the way the localizer would, and says what is wrong with what it finds.</summary>
    private FailureTranslationFinding? Inspect(CultureInfo culture, Expected expected)
    {
        var levels = Levels(culture);

        foreach (var key in expected.Keys)
        {
            // Your sources first, in order; each one falls back through its own culture chain before the
            // next is asked, because that is what IStringLocalizer's indexer does.
            foreach (var source in _localizer.Sources)
            {
                foreach (var level in levels)
                {
                    if (!Holds(source, level, key))
                    {
                        continue;
                    }

                    if (!ReachesTheNeutralResx(level) || SpeaksNeutral(culture))
                    {
                        return null;
                    }

                    return ToolkitTranslates(culture, key)
                        ? new(culture, key, FailureTranslationProblem.Hidden,
                            $"{source.Name} has it only in its neutral ({Neutral.Name}) resx, which wins over the toolkit's "
                            + $"{culture.TwoLetterISOLanguageName} text. Add it to the {culture.TwoLetterISOLanguageName} resx too.")
                        : new(culture, key, FailureTranslationProblem.FallsBack,
                            $"readers get {source.Name}'s neutral ({Neutral.Name}) text.");
                }
            }

            if (ToolkitTranslates(culture, key))
            {
                return null;
            }

            if (ToolkitHolds(CultureInfo.InvariantCulture, key))
            {
                return new(culture, key, FailureTranslationProblem.FallsBack, "readers get the toolkit's English text.");
            }
        }

        var keys = string.Join(" or ", expected.Keys);
        return new(culture, keys, FailureTranslationProblem.Missing, "no source knows it, so readers get the domain's own message.");
    }

    /// <summary>The culture itself, its parents, and last the neutral resx (the invariant culture).</summary>
    private static List<CultureInfo> Levels(CultureInfo culture)
    {
        var levels = new List<CultureInfo>();
        for (var level = culture; !Equals(level, CultureInfo.InvariantCulture); level = level.Parent)
        {
            levels.Add(level);
        }

        levels.Add(CultureInfo.InvariantCulture);
        return levels;
    }

    private CultureInfo Neutral => _cultures[0];

    private static bool ReachesTheNeutralResx(CultureInfo level) => Equals(level, CultureInfo.InvariantCulture);

    private bool SpeaksNeutral(CultureInfo culture)
        => culture.TwoLetterISOLanguageName == Neutral.TwoLetterISOLanguageName;

    /// <summary>Whether the toolkit has the key in the reader's own language, not just in English.</summary>
    private static bool ToolkitTranslates(CultureInfo culture, string key)
    {
        foreach (var level in Levels(culture))
        {
            if (ToolkitHolds(level, key))
            {
                // The toolkit's neutral resx is English, so it only counts for an English reader.
                return !ReachesTheNeutralResx(level) || culture.TwoLetterISOLanguageName == "en";
            }
        }

        return false;
    }

    private static bool ToolkitHolds(CultureInfo level, string key)
        => FailureLocalizer.ToolkitResources.GetResourceSet(level, createIfNotExists: true, tryParents: false)?.GetString(key) is not null;

    /// <summary>Whether <paramref name="source"/> holds the key at exactly this level, without its parents.</summary>
    private bool Holds(NamedSource source, CultureInfo level, string key)
    {
        if (!_held.TryGetValue((source, level.Name), out var keys))
        {
            keys = new HashSet<string>(StringComparer.Ordinal);
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = level;
                foreach (var entry in source.Localizer.GetAllStrings(includeParentCultures: false))
                {
                    keys.Add(entry.Name);
                }
            }
            catch (MissingManifestResourceException)
            {
                // The resx-backed localizer throws when a language has no resx at all, which is an
                // honest "holds nothing at this level".
            }
            finally
            {
                CultureInfo.CurrentUICulture = previous;
            }

            _held[(source, level.Name)] = keys;
        }

        return keys.Contains(key);
    }

    private static Type? InvariantInterface(Type type)
        => type.GetInterfaces().FirstOrDefault(@interface =>
            @interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(IInvariant<>));

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null)!;
        }
    }

    /// <summary>One failure that should be translated, and the keys the localizer tries for it, in order.</summary>
    private sealed record Expected(string[] Keys);
}
