using System.Diagnostics;
using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;

namespace Examples.Tenancy.Host.Requests;

/// <summary>
/// The source of the activities <see cref="RequestTracingBehavior{TMessage, TResponse}"/> starts: one per command
/// or query the host handles.
/// </summary>
/// <remarks>
/// One for the application, because a source is made once and listened to by name. Its name is the application's,
/// <see cref="IHostEnvironment.ApplicationName"/>, which is the one source of the host's own that the service
/// defaults listen to, so the activities reach the dashboard without the host naming a source anywhere else.
/// </remarks>
/// <param name="environment">Names the application.</param>
public sealed class RequestTracing(IHostEnvironment environment) : IDisposable
{
    /// <summary>
    /// The tag a request carries that was answered with something other than what it asked for: the code the
    /// answer has, such as <c>projects.not-permitted</c> or <c>concurrency-conflict</c>.
    /// </summary>
    public const string RefusalTag = "sample.refusal";

    /// <summary>
    /// The code <paramref name="thrown"/> is answered with, where the host answers it (<see cref="RefusalProblems"/>):
    /// a refusal's own, a value that broke its rules, the first rule an aggregate reports broken, and a lost race.
    /// <see langword="null"/> for anything else, which is a fault.
    /// </summary>
    /// <param name="thrown">What a request's pipeline threw.</param>
    public static string? AnswerOf(Exception thrown)
        => thrown switch
        {
            RefusalException refusal => refusal.Code,
            InvalidValueObjectException => RefusalProblems.InvalidValue,
            InvariantViolationException broken => broken.InvariantViolations is [var first, ..] ? first.Code : InvariantViolation.SeamCode,
            ConcurrencyConflictException => RefusalProblems.ConcurrencyConflict,
            _ => null,
        };

    private readonly ActivitySource _source = new(environment.ApplicationName);

    /// <summary>
    /// Starts the activity of one request, named after its type, or <see langword="null"/> when nobody listens.
    /// </summary>
    /// <param name="request">The type of the command or query.</param>
    public Activity? Start(Type request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _source.StartActivity(request.Name);
    }

    /// <inheritdoc />
    public void Dispose() => _source.Dispose();
}
