namespace DDDToolkit.Validation;

/// <summary>
/// Collects the failures of one validation run. A value object describes why it is invalid by
/// overriding <c>Validate(ValidationErrorBuilder)</c> and adding to the builder it is handed:
/// <code>
/// protected override void Validate(ValidationErrorBuilder errors)
/// {
///     if (Value.Length &gt; 40)
///     {
///         errors.Add("A street is at most 40 characters.", nameof(Value), "TooLong", Value,
///             new Dictionary&lt;string, object?&gt; { ["MaxLength"] = 40 });
///     }
/// }
/// </code>
/// <para>
/// A run that adds nothing is a pass. You never construct one yourself: the base type creates it,
/// passes it in and keeps the result in <c>ValidationErrors</c>.
/// </para>
/// </summary>
public sealed class ValidationErrorBuilder
{
    private readonly List<ValidationError> _errors = [];

    /// <summary>How many failures have been added so far.</summary>
    public int Count => _errors.Count;

    /// <summary>True once at least one failure has been added.</summary>
    public bool HasErrors => _errors.Count > 0;

    /// <summary>Adds a failure.</summary>
    /// <param name="error">The failure to add.</param>
    /// <returns>The same builder, so calls chain.</returns>
    public ValidationErrorBuilder Add(ValidationError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        _errors.Add(error);
        return this;
    }

    /// <summary>Adds a failure described in pieces.</summary>
    /// <param name="message">What is wrong, in words a caller could show.</param>
    /// <param name="propertyName">The property the failure belongs to, or <see langword="null"/> for the whole value.</param>
    /// <param name="code">A stable machine-readable code, so callers can branch without matching on text.</param>
    /// <param name="attemptedValue">The value that was rejected, when it is safe to repeat back.</param>
    /// <param name="arguments">The values the message was built from, by name, so it can be phrased again in another language.</param>
    /// <returns>The same builder, so calls chain.</returns>
    public ValidationErrorBuilder Add(
        string message,
        string? propertyName = null,
        string? code = null,
        object? attemptedValue = null,
        IEnumerable<KeyValuePair<string, object?>>? arguments = null)
        => Add(new ValidationError(message, propertyName, code, attemptedValue, arguments));

    /// <summary>Adds every failure in a sequence, which is how failures from a nested value object are folded in.</summary>
    /// <param name="errors">The failures to add. A <see langword="null"/> entry is skipped.</param>
    /// <returns>The same builder, so calls chain.</returns>
    public ValidationErrorBuilder AddRange(IEnumerable<ValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        foreach (var error in errors)
        {
            if (error is not null)
            {
                _errors.Add(error);
            }
        }

        return this;
    }

    /// <summary>The failures collected so far, as a list that cannot be written to afterwards.</summary>
    public IReadOnlyList<ValidationError> ToReadOnlyList() => _errors.ToArray();
}
