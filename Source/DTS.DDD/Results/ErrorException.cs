using DTS.DDD.Exceptions;

namespace DTS.DDD.Results;

/// <summary>
/// An <see cref="Error"/> as an exception, for code that has no result to return it in, such as
/// <see cref="Result{T}.Value"/> of a failure. The mediator and the GraphQL edge turn it back into the error.
/// </summary>
public sealed class ErrorException(Error error) : DDDToolkitException((error ?? throw new ArgumentNullException(nameof(error))).Message)
{
    /// <summary>The error.</summary>
    public Error Error { get; } = error;
}