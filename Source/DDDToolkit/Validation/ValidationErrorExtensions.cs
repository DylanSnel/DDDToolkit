namespace DDDToolkit.Validation;

/// <summary>
/// Shaping helpers for a collection of failures, for the case the toolkit cannot do for you: one
/// request carrying several value objects, where the caller wants every failure at once and wants to
/// know which field each one came from.
/// </summary>
public static class ValidationErrorExtensions
{
    /// <summary>The property name used for a failure that belongs to no particular property.</summary>
    public const string NoProperty = "";

    /// <summary>
    /// Re-labels every failure so its property name sits under <paramref name="prefix"/>. A value object
    /// reports its failures against its own properties (<c>Value</c>, <c>Street</c>), which says nothing
    /// about where it sat in the request; this puts it back.
    /// </summary>
    /// <param name="errors">The failures to re-label.</param>
    /// <param name="prefix">The name of the field the value object came from, such as <c>shipTo</c>.</param>
    /// <returns>A new list of failures; the originals are unchanged.</returns>
    /// <example>
    /// <code>
    /// // "Street" becomes "shipTo.Street"
    /// var all = shipToErrors.Prefixed("shipTo").Concat(emailErrors.Prefixed("email"));
    /// </code>
    /// </example>
    public static IReadOnlyList<ValidationError> Prefixed(this IEnumerable<ValidationError> errors, string prefix)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(prefix);

        return errors
            .Select(error => error with
            {
                PropertyName = string.IsNullOrEmpty(error.PropertyName)
                    ? prefix
                    : prefix + "." + error.PropertyName,
            })
            .ToArray();
    }

    /// <summary>
    /// Groups the failures by property name and keeps only their messages. That is the shape ASP.NET
    /// Core's <c>Results.ValidationProblem</c> and <c>ModelStateDictionary</c> expect, so a refusal
    /// becomes a 400 with a body the client can read field by field.
    /// </summary>
    /// <param name="errors">The failures to group.</param>
    /// <returns>Property name to messages. Failures with no property are grouped under <see cref="NoProperty"/>.</returns>
    public static Dictionary<string, string[]> ToErrorDictionary(this IEnumerable<ValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        return errors
            .GroupBy(error => error.PropertyName ?? NoProperty, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Message).ToArray(),
                StringComparer.Ordinal);
    }
}
