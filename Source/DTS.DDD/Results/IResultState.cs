namespace DTS.DDD.Results;

/// <summary>
/// What <see cref="Result"/> and <see cref="Result{T}"/> have in common, for code that handles every result without
/// knowing its value type: the GraphQL and HTTP edges and the logging.
/// </summary>
public interface IResultState
{
    /// <summary>Whether the work failed.</summary>
    bool IsFailure { get; }

    /// <summary>The error of a failure, or null after a success.</summary>
    Error? ErrorOrNull { get; }

    /// <summary>The value of a success, boxed, or null after a failure or for a <see cref="Result"/>.</summary>
    object? ValueOrNull { get; }
}