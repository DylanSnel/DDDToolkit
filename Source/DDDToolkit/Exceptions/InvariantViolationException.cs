using System.Collections.ObjectModel;

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

    /// <summary>Creates an exception with a message of your own and no aggregate attached.</summary>
    public InvariantViolationException(string message, Exception? innerException = null)
        : base(message, innerException)
        => Violations = ReadOnlyCollection<string>.Empty;

    private InvariantViolationException(Type? aggregateType, object? aggregateId, string[] violations, Exception? innerException)
        : base(BuildMessage(aggregateType, aggregateId, violations), innerException)
    {
        AggregateType = aggregateType;
        AggregateId = aggregateId;
        Violations = new ReadOnlyCollection<string>(violations);
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

    private static string[] ToArray(IEnumerable<string> violations)
    {
        ArgumentNullException.ThrowIfNull(violations);
        return violations as string[] ?? [.. violations];
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
