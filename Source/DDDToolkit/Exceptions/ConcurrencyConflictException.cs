namespace DDDToolkit.Exceptions;

/// <summary>
/// Thrown when an aggregate is saved with a stale <c>Version</c>: somebody else changed it since it
/// was loaded. Reload the aggregate, reapply the change and save again, or report the conflict.
/// </summary>
public class ConcurrencyConflictException : DDDToolkitException
{
    public ConcurrencyConflictException(Type? aggregateType, object? aggregateId, Exception? innerException = null)
        : base(BuildMessage(aggregateType, aggregateId), innerException)
    {
        AggregateType = aggregateType;
        AggregateId = aggregateId;
    }

    public ConcurrencyConflictException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    /// <summary>The CLR type of the aggregate that conflicted, when known.</summary>
    public Type? AggregateType { get; }

    /// <summary>The id of the aggregate that conflicted, when known.</summary>
    public object? AggregateId { get; }

    private static string BuildMessage(Type? aggregateType, object? aggregateId)
    {
        var name = aggregateType?.Name ?? "aggregate";
        return aggregateId is null
            ? $"The {name} was modified by another operation since it was loaded."
            : $"The {name} '{aggregateId}' was modified by another operation since it was loaded.";
    }
}
