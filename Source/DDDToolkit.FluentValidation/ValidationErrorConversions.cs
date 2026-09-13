using DDDToolkit.Validation;
using FluentValidation.Results;

namespace DDDToolkit.FluentValidation;

/// <summary>
/// Turns FluentValidation's failures into the toolkit's own <see cref="ValidationError"/>.
/// <para>
/// Value objects do this for you: the generated <c>Validate()</c> copies its failures into
/// <c>ValidationErrors</c>, which is what <c>TryToValid()</c> hands back. These methods are for the
/// validator you write yourself — the one for a command or a request DTO — so its result can join the
/// same list and be shaped by <see cref="ValidationErrorExtensions.ToErrorDictionary"/> with everything
/// else.
/// </para>
/// </summary>
public static class ValidationErrorConversions
{
    /// <summary>Converts one FluentValidation failure, keeping the property, code and attempted value.</summary>
    /// <param name="failure">The failure to convert.</param>
    /// <returns>The same failure in the toolkit's shape.</returns>
    public static ValidationError ToValidationError(this ValidationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new ValidationError(
            failure.ErrorMessage,
            failure.PropertyName,
            failure.ErrorCode,
            failure.AttemptedValue);
    }

    /// <summary>Converts every failure in a sequence.</summary>
    /// <param name="failures">The failures to convert.</param>
    /// <returns>The failures in the toolkit's shape.</returns>
    public static IReadOnlyList<ValidationError> ToValidationErrors(this IEnumerable<ValidationFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);

        return failures.Select(ToValidationError).ToArray();
    }

    /// <summary>Converts the failures of a validation run. An empty list means the run passed.</summary>
    /// <param name="result">The result of running a validator.</param>
    /// <returns>The failures in the toolkit's shape.</returns>
    public static IReadOnlyList<ValidationError> ToValidationErrors(this ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Errors.ToValidationErrors();
    }
}
