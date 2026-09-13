namespace DDDToolkit.Testing;

/// <summary>
/// Thrown when an assertion in this package fails. Every message names what was expected and lists
/// the events that were actually raised, so the failure is readable without opening a debugger.
/// </summary>
/// <remarks>
/// It derives from <see cref="Exception"/> rather than from a test framework's assertion type on
/// purpose: this package has no test framework dependency, and every runner reports an unhandled
/// exception as a failed test. It does not derive from <c>DDDToolkitException</c> either, because
/// this is a statement about a test, not about the domain.
/// </remarks>
public sealed class AggregateAssertionException : Exception
{
    /// <summary>Creates the exception with a ready-made message.</summary>
    /// <param name="message">The failure message. Include both the expectation and what happened.</param>
    public AggregateAssertionException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the exception that caused the failure.</summary>
    /// <param name="message">The failure message.</param>
    /// <param name="innerException">
    /// The exception the aggregate actually threw, kept so its stack trace survives.
    /// </param>
    public AggregateAssertionException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
