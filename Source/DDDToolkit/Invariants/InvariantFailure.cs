using DDDToolkit.Validation;

namespace DDDToolkit.Invariants;

/// <summary>
/// What <see cref="IInvariant{TEntity}.Check"/> returns when its rule does not hold: the message, and the
/// values it was built from so it can be phrased again in another language.
/// <para>
/// You rarely construct one. A string converts to it, so a rule that only has a message returns the
/// message and a rule that holds returns <see langword="null"/>:
/// </para>
/// <code>
/// public InvariantFailure? Check(Order order)
///     =&gt; order._lines.Count == 0 ? "An order must have at least one line." : null;
/// </code>
/// <para>
/// A rule whose message names a value adds that value with <see cref="With"/>, so a translation can put
/// it where its own grammar wants it:
/// </para>
/// <code>
/// public InvariantFailure? Check(Order order)
///     =&gt; order._lines.Count &lt;= Max
///         ? null
///         : new InvariantFailure($"An order holds at most {Max} lines.").With("Max", Max);
/// </code>
/// <para>
/// A class rather than a struct so that holding stays free: the happy path returns
/// <see langword="null"/> and allocates nothing, exactly as it did when the rule returned a string.
/// </para>
/// </summary>
public sealed class InvariantFailure
{
    /// <summary>Creates a failure.</summary>
    /// <param name="message">What is wrong, in the domain's own words.</param>
    /// <param name="arguments">The values the message was built from, by name.</param>
    public InvariantFailure(string message, IEnumerable<KeyValuePair<string, object?>>? arguments = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        Message = message;
        Arguments = FailureArguments.Copy(arguments);
    }

    private InvariantFailure(string message, IReadOnlyDictionary<string, object?> arguments)
    {
        Message = message;
        Arguments = arguments;
    }

    /// <summary>What is wrong, in the domain's own words.</summary>
    public string Message { get; }

    /// <summary>The values <see cref="Message"/> was built from, by name. Empty when there are none.</summary>
    public IReadOnlyDictionary<string, object?> Arguments { get; }

    /// <summary>A copy of this failure with one more argument.</summary>
    /// <param name="name">The argument's name, as a template would spell it between braces.</param>
    /// <param name="value">Its value.</param>
    public InvariantFailure With(string name, object? value)
        => new(Message, FailureArguments.With(Arguments, name, value));

    /// <summary>
    /// Lets a rule return its message as it always could. A <see langword="null"/> string stays
    /// <see langword="null"/>, so <c>holds ? null : "message"</c> still means "holds" on the happy path.
    /// </summary>
    /// <param name="message">The message, or <see langword="null"/> when the rule holds.</param>
    public static implicit operator InvariantFailure?(string? message)
        => message is null ? null : new InvariantFailure(message);

    /// <summary>The message.</summary>
    public override string ToString() => Message;
}
