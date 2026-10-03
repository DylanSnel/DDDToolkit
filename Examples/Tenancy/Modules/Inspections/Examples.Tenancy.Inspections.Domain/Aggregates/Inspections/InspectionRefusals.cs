using System.Globalization;
using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;

namespace Examples.Tenancy.Inspections.Domain.Aggregates.Inspections;

/// <summary>
/// Every way Inspections refuses a command, as codes a client branches on, with the kind that decides the HTTP
/// status and the English text a client may show.
/// </summary>
/// <remarks>
/// The title, the days and the two about a page of its list are Inspections' own rules. Three others are Projects'
/// answers about the project, which Inspections passes on under Projects' own codes: a client that is refused a
/// project reads the same code whether it tried to rename the project or to record an inspection on it, and a
/// project the caller may not see is not found here exactly as it is not found there. The codes are written out again rather than referenced,
/// because they are Projects' module's own and only its gate is published; a test holds the two modules to
/// the same codes and the same English.
/// <para>
/// An argument is a value, such as a key or a length, never a word, so a translation can be a sentence of its own.
/// The translations of Inspections' own codes are beside this class, in the two resource files of
/// <see cref="InspectionFailures"/>; those of the three codes passed on are Projects', so a code has one text.
/// </para>
/// </remarks>
public static class InspectionRefusals
{
    /// <summary>The title is blank or too long. Argument: <c>Max</c>.</summary>
    public const string TitleInvalid = "inspections.title-invalid";

    /// <summary>The days the inspection covers lack a day, or the last is before the first.</summary>
    public const string DaysInvalid = "inspections.days-invalid";

    /// <summary>
    /// The project is planned for a range of days, and the inspection's days do not all lie within it. Inspections'
    /// own code for a rule that crosses the modules: the range is Projects' answer, the refusal is this module's.
    /// Arguments: <c>From</c> and <c>Until</c>, the planned range as ISO dates.
    /// </summary>
    public const string OutsidePlannedRange = "inspections.outside-planned-range";

    /// <summary>
    /// Projects' code for a project the caller may not see: it does not exist, belongs to another tenant, or is
    /// out of the caller's reach. One answer for all three.
    /// </summary>
    public const string ProjectNotFound = "projects.not-found";

    /// <summary>Projects' code for a project the caller may see but not do this to. Argument: <c>Key</c>.</summary>
    public const string ProjectNotPermitted = "projects.not-permitted";

    /// <summary>Projects' code for a closed project, about which nothing changes until it is reopened.</summary>
    public const string ProjectClosed = "projects.closed";

    /// <summary>The marker a page of a project's inspections was asked with is not a cursor that list gave.</summary>
    public const string CursorInvalid = "inspections.cursor-invalid";

    /// <summary>A page of a project's inspections was asked with a size out of range. Argument: <c>Max</c>.</summary>
    public const string PageSizeInvalid = "inspections.page-size-invalid";

    /// <summary>A page of a project's inspections was asked for by its first rows and by its last at once.</summary>
    public const string PageFromBothEnds = "inspections.page-from-both-ends";

    private static readonly Dictionary<string, (RefusalKind Kind, string Text)> Table = new(StringComparer.Ordinal)
    {
        [TitleInvalid] = (RefusalKind.Invalid, "An inspection's title is 1 to {Max} characters."),
        [DaysInvalid] = (RefusalKind.Invalid, "The days an inspection covers have a first and a last day, and the last is not before the first."),
        [OutsidePlannedRange] = (RefusalKind.Conflict, "The project is planned from {From} to {Until}; an inspection covers days within that. Without days it covers the day it is recorded, by the clock in UTC."),
        [ProjectNotFound] = (RefusalKind.NotFound, "There is no such project, or it is not one you can see."),
        [ProjectNotPermitted] = (RefusalKind.NotPermitted, "Doing this to the project needs the key {Key}."),
        [ProjectClosed] = (RefusalKind.Conflict, "The project is closed; nothing about it changes until it is reopened."),
        [CursorInvalid] = (RefusalKind.Invalid, "That page marker does not belong to this list. Start again from the first page."),
        [PageSizeInvalid] = (RefusalKind.Invalid, "A page holds 1 to {Max} inspections."),
        [PageFromBothEnds] = (RefusalKind.Invalid, "A page is the first of a list or the last of it. Ask with first or with last, not with both."),
    };

    /// <summary>Every code above, Projects' three included.</summary>
    public static IReadOnlyList<string> Codes { get; } = [.. Table.Keys];

    /// <summary>
    /// The English text of <paramref name="code"/> with its placeholders as written, such as <c>{Max}</c>: what a
    /// test holds the resource files to.
    /// </summary>
    /// <param name="code">One of the codes above.</param>
    /// <exception cref="ArgumentException"><paramref name="code"/> is not one of the codes above.</exception>
    public static string TemplateOf(string code) => TextOf(code).Text;

    /// <summary>The refusal with <paramref name="code"/>, its kind and English text, and <paramref name="arguments"/>.</summary>
    /// <param name="code">One of the codes above.</param>
    /// <param name="arguments">The values the text shows, by name.</param>
    /// <exception cref="ArgumentException"><paramref name="code"/> is not one of the codes above.</exception>
    public static RefusalException Of(string code, params (string Name, object? Value)[] arguments)
    {
        var (kind, text) = TextOf(code);
        var named = Named(arguments);
        return new RefusalException(code, kind, Fill(text, named), named);
    }

    /// <summary>
    /// What a nested invariant of <see cref="Inspection"/> reports when the rule behind <paramref name="code"/>
    /// does not hold: the refusal's text and arguments, so one translation serves both.
    /// </summary>
    internal static InvariantFailure Failure(string code, params (string Name, object? Value)[] arguments)
    {
        var named = Named(arguments);
        return new InvariantFailure(Fill(TextOf(code).Text, named), named);
    }

    private static (RefusalKind Kind, string Text) TextOf(string code)
        => Table.TryGetValue(code, out var row) ? row : throw new ArgumentException("'" + code + "' is not one of Inspections' refusal codes.", nameof(code));

    private static Dictionary<string, object?> Named((string Name, object? Value)[] arguments)
    {
        var named = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in arguments)
        {
            named[name] = value;
        }

        return named;
    }

    /// <summary>Fills each <c>{Name}</c> in the invariant culture: the English text is the domain's own.</summary>
    private static string Fill(string text, IReadOnlyDictionary<string, object?> arguments)
    {
        foreach (var (name, value) in arguments)
        {
            text = text.Replace("{" + name + "}", Convert.ToString(value, CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        return text;
    }
}
