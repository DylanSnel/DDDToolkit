using System.Globalization;
using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;
using DDDToolkit.Validation;

namespace DDDToolkit.Localization;

/// <summary>
/// Puts a whole list of failures into the reader's language at once, at the point where it becomes a
/// response, and phrases one failure in a language the caller names, where there is no request to take the
/// language from.
/// <code>
/// if (!command.ShipTo.TryToValid(out var shipTo, out var errors))
/// {
///     return Results.ValidationProblem(errors.Prefixed("shipTo").ToErrorDictionary(localizer));
/// }
/// </code>
/// </summary>
public static class FailureLocalizationExtensions
{
    /// <summary>
    /// The same failures with each <see cref="ValidationError.Message"/> in the reader's language. Code,
    /// property, attempted value and arguments are kept, so the result can still be branched on.
    /// </summary>
    /// <param name="errors">The failures to phrase.</param>
    /// <param name="localizer">What phrases them.</param>
    public static IReadOnlyList<ValidationError> Localized(this IEnumerable<ValidationError> errors, IFailureLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(localizer);

        return errors.Select(error => error with { Message = localizer.Localize(error) }).ToArray();
    }

    /// <summary>
    /// The same violations with each <see cref="InvariantViolation.Message"/> in the reader's language.
    /// Code, entity and arguments are kept.
    /// </summary>
    /// <param name="violations">
    /// The violations to phrase: from <c>GetInvariantViolations()</c>, or
    /// <c>InvariantViolationException.InvariantViolations</c> on the throwing path.
    /// </param>
    /// <param name="localizer">What phrases them.</param>
    public static IReadOnlyList<InvariantViolation> Localized(this IEnumerable<InvariantViolation> violations, IFailureLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(violations);
        ArgumentNullException.ThrowIfNull(localizer);

        return violations.Select(violation => violation with { Message = localizer.Localize(violation) }).ToArray();
    }

    /// <summary>
    /// <see cref="ValidationErrorExtensions.ToErrorDictionary"/> in the reader's language: property name
    /// to translated messages, the shape <c>Results.ValidationProblem</c> expects.
    /// </summary>
    /// <param name="errors">The failures to group.</param>
    /// <param name="localizer">What phrases them.</param>
    public static Dictionary<string, string[]> ToErrorDictionary(this IEnumerable<ValidationError> errors, IFailureLocalizer localizer)
        => errors.Localized(localizer).ToErrorDictionary();

    /// <summary>
    /// The refusal in <paramref name="culture"/>, whatever the current UI culture is: for a text written
    /// outside a request, such as a mail that says why a job stopped, and for an exception handler, which runs
    /// after the culture a request's middleware chose has gone.
    /// <para>
    /// The localizer is asked inside <see cref="CultureScope.Use(CultureInfo)"/>, so a localizer of the
    /// application's own, which reads the current UI culture like any other, is honored too.
    /// </para>
    /// </summary>
    /// <param name="localizer">What phrases the refusal.</param>
    /// <param name="refusal">The refusal to phrase.</param>
    /// <param name="culture">The reader's culture: the language of the text, and how its values are written.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static string Localize(this IFailureLocalizer localizer, RefusalException refusal, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(refusal);

        using (CultureScope.Use(culture))
        {
            return localizer.Localize(refusal);
        }
    }

    /// <summary>
    /// The failure in <paramref name="culture"/>, whatever the current UI culture is. See
    /// <see cref="Localize(IFailureLocalizer, RefusalException, CultureInfo)"/>.
    /// </summary>
    /// <param name="localizer">What phrases the failure.</param>
    /// <param name="error">The failure to phrase.</param>
    /// <param name="culture">The reader's culture: the language of the text, and how its values are written.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static string Localize(this IFailureLocalizer localizer, ValidationError error, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(error);

        using (CultureScope.Use(culture))
        {
            return localizer.Localize(error);
        }
    }

    /// <summary>
    /// The violation in <paramref name="culture"/>, whatever the current UI culture is. See
    /// <see cref="Localize(IFailureLocalizer, RefusalException, CultureInfo)"/>.
    /// </summary>
    /// <param name="localizer">What phrases the violation.</param>
    /// <param name="violation">The violation to phrase.</param>
    /// <param name="culture">The reader's culture: the language of the text, and how its values are written.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static string Localize(this IFailureLocalizer localizer, InvariantViolation violation, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(violation);

        using (CultureScope.Use(culture))
        {
            return localizer.Localize(violation);
        }
    }
}
