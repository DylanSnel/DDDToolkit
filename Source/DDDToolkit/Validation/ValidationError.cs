namespace DDDToolkit.Validation;

/// <summary>
/// One reason a value object is not valid.
/// <para>
/// This is the toolkit's own failure shape, so that reading why something is invalid does not require
/// FluentValidation. When FluentValidation is referenced, every
/// <c>FluentValidation.Results.ValidationFailure</c> the generated validator produces is copied into one
/// of these, so the same code reads failures either way.
/// </para>
/// <para>
/// It is deliberately not a <c>Result&lt;T&gt;</c>. The toolkit hands you the failures and lets you put
/// them in whatever result type your application already uses.
/// </para>
/// </summary>
public sealed record ValidationError
{
    /// <summary>
    /// The code used when a <c>Validate()</c> override said "no" without saying why. Branch on it to
    /// spot value objects whose rules could be more descriptive.
    /// </summary>
    public const string UnspecifiedCode = "Unspecified";

    /// <summary>Creates a failure.</summary>
    /// <param name="message">What is wrong, in words a caller could show.</param>
    /// <param name="propertyName">The property the failure belongs to, or <see langword="null"/> when it is about the whole value.</param>
    /// <param name="code">A stable machine-readable code, so callers can branch without matching on text.</param>
    /// <param name="attemptedValue">The value that was rejected, when it is safe to repeat back.</param>
    public ValidationError(string message, string? propertyName = null, string? code = null, object? attemptedValue = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        Message = message;
        PropertyName = propertyName;
        Code = code;
        AttemptedValue = attemptedValue;
    }

    /// <summary>What is wrong, in words a caller could show.</summary>
    public string Message { get; init; }

    /// <summary>The property the failure belongs to, or <see langword="null"/> when it is about the whole value.</summary>
    public string? PropertyName { get; init; }

    /// <summary>A stable machine-readable code, or <see langword="null"/> when the rule supplied none.</summary>
    public string? Code { get; init; }

    /// <summary>The value that was rejected, when the rule reported one.</summary>
    public object? AttemptedValue { get; init; }

    /// <summary>The failure as <c>PropertyName: Message</c>, or just the message when there is no property.</summary>
    public override string ToString()
        => string.IsNullOrEmpty(PropertyName) ? Message : PropertyName + ": " + Message;
}
