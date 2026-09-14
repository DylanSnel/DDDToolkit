using System.Diagnostics.CodeAnalysis;
using DDDToolkit.BaseTypes;

namespace DDDToolkit.Validation;

/// <summary>
/// The non-throwing half of the failure model. <c>ToValid()</c> and <c>EnsureValidated()</c> still throw
/// <see cref="Exceptions.InvalidValueObjectException"/>; these members hand back the failures instead, so
/// an API layer can answer with a refusal rather than a 500, and a validation pass can collect every
/// failure in a request before it answers.
/// <para>
/// They are extension methods rather than generated members on purpose. Nothing lands on your types, so
/// nothing new turns up in an Entity Framework model or a GraphQL schema, and the toolkit stays out of
/// your signatures: it gives you the failures and you put them in whichever result type you already use.
/// </para>
/// </summary>
public static class ValidationExtensions
{
    private static readonly ValidationError[] None = [];

    /// <summary>
    /// Converts a value object into its always-valid twin, or reports why it cannot be converted.
    /// </summary>
    /// <typeparam name="TValid">The twin type, inferred from the value object.</typeparam>
    /// <param name="value">The value object to convert.</param>
    /// <param name="valid">The twin, when this returns <see langword="true"/>; otherwise <see langword="null"/>.</param>
    /// <param name="errors">Every reason the value is invalid, when this returns <see langword="false"/>; otherwise empty.</param>
    /// <returns><see langword="true"/> when the value satisfied its own rules.</returns>
    /// <example>
    /// <code>
    /// if (!EmailAddress.Create(input).TryToValid(out var email, out var errors))
    /// {
    ///     return Results.ValidationProblem(errors.ToErrorDictionary());
    /// }
    /// </code>
    /// </example>
    public static bool TryToValid<TValid>(
        this IValidatable<TValid> value,
        [NotNullWhen(true)] out TValid? valid,
        out IReadOnlyList<ValidationError> errors)
        where TValid : class
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.IsValid)
        {
            valid = value.ToValid();
            errors = None;
            return true;
        }

        valid = null;
        errors = value.ValidationErrors;
        return false;
    }

    /// <summary>
    /// Converts a value object into its always-valid twin, for a caller that only needs to know whether
    /// it worked.
    /// </summary>
    /// <typeparam name="TValid">The twin type, inferred from the value object.</typeparam>
    /// <param name="value">The value object to convert.</param>
    /// <param name="valid">The twin, when this returns <see langword="true"/>; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the value satisfied its own rules.</returns>
    public static bool TryToValid<TValid>(this IValidatable<TValid> value, [NotNullWhen(true)] out TValid? valid)
        where TValid : class
        => value.TryToValid(out valid, out _);

    /// <summary>
    /// Runs the value object's rules without throwing and hands back the failures. Use it for a value
    /// object you do not intend to convert, such as one nested inside another.
    /// </summary>
    /// <param name="value">The value object to check.</param>
    /// <param name="errors">Every reason the value is invalid; empty when it is valid.</param>
    /// <returns><see langword="true"/> when the value satisfied its own rules.</returns>
    public static bool TryValidate(this ValueObject value, out IReadOnlyList<ValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.IsValid)
        {
            errors = None;
            return true;
        }

        errors = value.ValidationErrors;
        return false;
    }
}
