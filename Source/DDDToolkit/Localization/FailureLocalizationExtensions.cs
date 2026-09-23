using DDDToolkit.Invariants;
using DDDToolkit.Validation;

namespace DDDToolkit.Localization;

/// <summary>
/// Puts a whole list of failures into the reader's language at once, at the point where it becomes a
/// response.
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
}
