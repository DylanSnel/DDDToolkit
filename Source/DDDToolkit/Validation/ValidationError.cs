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

    /// <summary>
    /// The argument that names the value object type a failure is about, on the failures the toolkit
    /// writes itself: <see cref="UnspecifiedCode"/> and <c>DDDToolkit.FluentValidation</c>'s
    /// <c>MustBeValid()</c>. A template reads it as <c>{ValueObject}</c>.
    /// </summary>
    public const string ValueObjectArgument = "ValueObject";

    /// <summary>Creates a failure.</summary>
    /// <param name="message">What is wrong, in words a caller could show.</param>
    /// <param name="propertyName">The property the failure belongs to, or <see langword="null"/> when it is about the whole value.</param>
    /// <param name="code">A stable machine-readable code, so callers can branch without matching on text.</param>
    /// <param name="attemptedValue">The value that was rejected, when it is safe to repeat back.</param>
    /// <param name="arguments">
    /// The values the message was built from, by name, such as <c>MaxLength</c>. They are what lets the
    /// message be phrased again in another language; see <see cref="Arguments"/>.
    /// </param>
    public ValidationError(
        string message,
        string? propertyName = null,
        string? code = null,
        object? attemptedValue = null,
        IEnumerable<KeyValuePair<string, object?>>? arguments = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        Message = message;
        PropertyName = propertyName;
        Code = code;
        AttemptedValue = attemptedValue;
        _arguments = FailureArguments.Copy(arguments);
    }

    private readonly IReadOnlyDictionary<string, object?> _arguments;

    /// <summary>What is wrong, in words a caller could show.</summary>
    public string Message { get; init; }

    /// <summary>The property the failure belongs to, or <see langword="null"/> when it is about the whole value.</summary>
    public string? PropertyName { get; init; }

    /// <summary>A stable machine-readable code, or <see langword="null"/> when the rule supplied none.</summary>
    public string? Code { get; init; }

    /// <summary>The value that was rejected, when the rule reported one.</summary>
    public object? AttemptedValue { get; init; }

    /// <summary>
    /// The values <see cref="Message"/> was built from, by name, matched without regard to case. Empty
    /// when the rule supplied none; never <see langword="null"/>.
    /// <para>
    /// <see cref="Message"/> is one sentence in one language with the numbers already in it. A translation
    /// needs the numbers on their own, so a localizer can look the failure up by <see cref="Code"/> and
    /// fill a template such as <c>"{PropertyName} is at most {MaxLength} characters."</c> from these.
    /// FluentValidation's placeholder values arrive here unchanged.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, object?> Arguments
    {
        get => _arguments;
        init => _arguments = FailureArguments.Copy(value);
    }

    /// <summary>A copy of this failure with one more argument, or with <paramref name="name"/> set to a new value.</summary>
    /// <param name="name">The argument's name, as a template would spell it between braces.</param>
    /// <param name="value">Its value.</param>
    public ValidationError With(string name, object? value)
        => this with { Arguments = FailureArguments.With(_arguments, name, value) };

    /// <summary>Equal when every member is, <see cref="Arguments"/> compared by content rather than by reference.</summary>
    public bool Equals(ValidationError? other)
        => other is not null
           && Message == other.Message
           && PropertyName == other.PropertyName
           && Code == other.Code
           && Equals(AttemptedValue, other.AttemptedValue)
           && FailureArguments.AreEqual(_arguments, other._arguments);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(Message, PropertyName, Code, AttemptedValue, FailureArguments.GetHashCode(_arguments));

    /// <summary>The failure as <c>PropertyName: Message</c>, or just the message when there is no property.</summary>
    public override string ToString()
        => string.IsNullOrEmpty(PropertyName) ? Message : PropertyName + ": " + Message;
}
