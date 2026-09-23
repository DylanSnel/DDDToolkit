using System.Collections.ObjectModel;
using DDDToolkit.Invariants;

namespace DDDToolkit.Exceptions;

/// <summary>
/// Thrown when an aggregate breaks one of its own invariants: a rule that must hold for the whole
/// aggregate after every change, not just for one property at the moment it is set.
/// <para>
/// This is not a validation failure. Validation says "this input is not acceptable" and belongs to
/// the edge of the system; an invariant violation says "the aggregate is in a state the domain says
/// cannot exist", which is a bug in the aggregate's own methods or in the code that called them. It
/// is also not a <see cref="ConcurrencyConflictException"/>: retrying will not help.
/// </para>
/// </summary>
public class InvariantViolationException : DDDToolkitException
{
    /// <summary>Creates an exception for a single broken rule.</summary>
    /// <param name="aggregateType">The CLR type of the aggregate, when known.</param>
    /// <param name="aggregateId">The id of the aggregate, when known.</param>
    /// <param name="violation">What must have been true and was not, in the domain's own words.</param>
    /// <param name="innerException">The exception that caused this one, if any.</param>
    public InvariantViolationException(Type? aggregateType, object? aggregateId, string violation, Exception? innerException = null)
        : this(aggregateType, aggregateId, new[] { violation ?? throw new ArgumentNullException(nameof(violation)) }, innerException)
    {
    }

    /// <summary>Creates an exception for several broken rules found in one check.</summary>
    /// <param name="aggregateType">The CLR type of the aggregate, when known.</param>
    /// <param name="aggregateId">The id of the aggregate, when known.</param>
    /// <param name="violations">Every rule that was broken, in the domain's own words.</param>
    /// <param name="innerException">The exception that caused this one, if any.</param>
    public InvariantViolationException(Type? aggregateType, object? aggregateId, IEnumerable<string> violations, Exception? innerException = null)
        : this(aggregateType, aggregateId, ToArray(violations), innerException)
    {
    }

    /// <summary>
    /// Creates an exception for the violations an invariant check found, keeping each one whole: its
    /// code, the entity that reported it and the arguments its message was built from. This is the
    /// constructor the generated <c>EnsureInvariants()</c> throws with, so the throwing path can be
    /// translated as well as the asking one.
    /// </summary>
    /// <param name="aggregateType">The CLR type of the aggregate, when known.</param>
    /// <param name="aggregateId">The id of the aggregate, when known.</param>
    /// <param name="violations">
    /// Everything that was found. A violation reported by this aggregate itself is phrased by its message
    /// alone, because the exception already names the aggregate; one reported by a child is phrased with
    /// the child's type and id, because nothing else would say which child it was.
    /// </param>
    /// <param name="innerException">The exception that caused this one, if any.</param>
    public InvariantViolationException(Type? aggregateType, object? aggregateId, IEnumerable<InvariantViolation> violations, Exception? innerException = null)
        : this(aggregateType, aggregateId, ToArray(violations), innerException)
    {
    }

    /// <summary>Creates an exception with a message of your own and no aggregate attached.</summary>
    public InvariantViolationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Violations = ReadOnlyCollection<string>.Empty;
        InvariantViolations = ReadOnlyCollection<InvariantViolation>.Empty;
    }

    private InvariantViolationException(Type? aggregateType, object? aggregateId, string[] violations, Exception? innerException)
        : this(aggregateType, aggregateId, Wrap(aggregateType, aggregateId, violations), violations, innerException)
    {
    }

    private InvariantViolationException(Type? aggregateType, object? aggregateId, InvariantViolation[] violations, Exception? innerException)
        : this(aggregateType, aggregateId, violations, Phrase(aggregateType, aggregateId, violations), innerException)
    {
    }

    private InvariantViolationException(Type? aggregateType, object? aggregateId, InvariantViolation[] violations, string[] phrased, Exception? innerException)
        : base(BuildMessage(aggregateType, aggregateId, phrased), innerException)
    {
        AggregateType = aggregateType;
        AggregateId = aggregateId;
        Violations = new ReadOnlyCollection<string>(phrased);
        InvariantViolations = new ReadOnlyCollection<InvariantViolation>(violations);
    }

    /// <summary>The CLR type of the aggregate that is inconsistent, when known.</summary>
    public Type? AggregateType { get; }

    /// <summary>The id of the aggregate that is inconsistent, when known.</summary>
    public object? AggregateId { get; }

    /// <summary>
    /// Every rule the check found broken, in the order the aggregate reported them. Empty when the
    /// exception was built from a message rather than from a list of rules.
    /// </summary>
    public IReadOnlyList<string> Violations { get; }

    /// <summary>
    /// The same rules as <see cref="Violations"/>, one for one and in the same order, but whole: each with
    /// its <see cref="InvariantViolation.Code"/>, the entity that reported it and its
    /// <see cref="InvariantViolation.Arguments"/>. This is what a handler turning the exception into a
    /// response reads, and what a localizer translates. A rule that was given only as a string carries
    /// <see cref="InvariantViolation.SeamCode"/>. Empty when the exception was built from a message.
    /// </summary>
    public IReadOnlyList<InvariantViolation> InvariantViolations { get; }

    private static string[] ToArray(IEnumerable<string> violations)
    {
        ArgumentNullException.ThrowIfNull(violations);
        return violations as string[] ?? [.. violations];
    }

    private static InvariantViolation[] ToArray(IEnumerable<InvariantViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(violations);
        return [.. violations];
    }

    private static InvariantViolation[] Wrap(Type? aggregateType, object? aggregateId, string[] violations)
    {
        var wrapped = new InvariantViolation[violations.Length];
        for (var index = 0; index < violations.Length; index++)
        {
            ArgumentNullException.ThrowIfNull(violations[index], nameof(violations));
            wrapped[index] = new InvariantViolation(InvariantViolation.SeamCode, violations[index], aggregateType, aggregateId);
        }

        return wrapped;
    }

    private static string[] Phrase(Type? aggregateType, object? aggregateId, InvariantViolation[] violations)
    {
        var phrased = new string[violations.Length];
        for (var index = 0; index < violations.Length; index++)
        {
            var violation = violations[index] ?? throw new ArgumentNullException(nameof(violations));
            var own = violation.EntityType is null
                      || (violation.EntityType == aggregateType && Equals(violation.EntityId, aggregateId));

            phrased[index] = own ? violation.Message : violation.ToString();
        }

        return phrased;
    }

    private static string BuildMessage(Type? aggregateType, object? aggregateId, IReadOnlyList<string> violations)
    {
        var name = aggregateType?.Name ?? "aggregate";
        var subject = aggregateId is null ? $"The {name}" : $"The {name} '{aggregateId}'";

        return violations.Count switch
        {
            0 => $"{subject} broke one of its invariants.",
            1 => $"{subject} broke an invariant: {violations[0]}",
            _ => $"{subject} broke {violations.Count} invariants: {string.Join(" ", violations)}",
        };
    }
}
