using System.Globalization;

namespace DDDToolkit.Localization;

/// <summary>What is wrong with one failure's translation in one language.</summary>
public enum FailureTranslationProblem
{
    /// <summary>No source knows the key at all, so readers get the domain's own message.</summary>
    Missing,

    /// <summary>
    /// Nothing translates the key into this language, so readers get the text of another language: your
    /// neutral resx, or the toolkit's English.
    /// </summary>
    FallsBack,

    /// <summary>
    /// The toolkit translates the key into this language, but your neutral resx overrides it first, so
    /// readers get your neutral text instead. Add the key to your resx for this language as well.
    /// </summary>
    Hidden,
}

/// <summary>One failure key that does not read right in one language.</summary>
/// <param name="Culture">The language it was checked in.</param>
/// <param name="Key">The key that was looked up: a failure code, or <c>{EntityType}.{Code}</c> for an invariant.</param>
/// <param name="Problem">What is wrong.</param>
/// <param name="Detail">A sentence saying what readers get instead and how to fix it.</param>
public sealed record FailureTranslationFinding(CultureInfo Culture, string Key, FailureTranslationProblem Problem, string Detail)
{
    /// <summary>Reads as one line of the report: culture, key, problem and detail.</summary>
    public override string ToString() => $"{Culture.Name,-6} {Key,-32} {Problem.ToString().ToLowerInvariant()}: {Detail}";
}

/// <summary>
/// Thrown by <see cref="FailureTranslations.Verify"/> when a failure is not translated in every language
/// you said you support. The message is the whole report; <see cref="Findings"/> holds it as data.
/// </summary>
public sealed class MissingFailureTranslationsException : Exception
{
    /// <summary>Creates the exception for a list of findings.</summary>
    /// <param name="findings">Everything the check found.</param>
    public MissingFailureTranslationsException(IReadOnlyList<FailureTranslationFinding> findings)
        : base(Describe(findings))
        => Findings = findings;

    /// <summary>Everything the check found, in culture order and then key order.</summary>
    public IReadOnlyList<FailureTranslationFinding> Findings { get; }

    private static string Describe(IReadOnlyList<FailureTranslationFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var header = findings.Count == 1
            ? "1 failure translation is missing or hidden:"
            : findings.Count + " failure translations are missing or hidden:";

        return header + Environment.NewLine + Environment.NewLine
               + string.Join(Environment.NewLine, findings.Select(finding => "  " + finding));
    }
}
