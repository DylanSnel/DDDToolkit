using DDDToolkit.Validation;

namespace DDDToolkit.Exceptions;

/// <summary>
/// Thrown when a value object is asked for its always-valid twin, or told to
/// <c>EnsureValidated()</c>, while it does not satisfy its own rules.
/// <para>
/// Catching this is the throwing path. When a refusal is an ordinary answer rather than a bug — an API
/// endpoint validating a request body, say — use <c>TryToValid</c> instead and never construct the
/// exception at all. See <c>docs/value-objects.md</c>.
/// </para>
/// </summary>
public class InvalidValueObjectException : DDDToolkitException
{
    /// <summary>Creates the exception without failure detail.</summary>
    /// <param name="objectType">The value object type that refused.</param>
    public InvalidValueObjectException(Type objectType)
        : this(objectType, [])
    {
    }

    /// <summary>Creates the exception carrying every reason the value was refused.</summary>
    /// <param name="objectType">The value object type that refused.</param>
    /// <param name="errors">The failures from the validation run.</param>
    public InvalidValueObjectException(Type objectType, IReadOnlyList<ValidationError> errors)
        : base(Describe(objectType, errors))
    {
        ObjectType = objectType;
        Errors = errors ?? [];
    }

    /// <summary>The value object type that refused.</summary>
    public Type ObjectType { get; }

    /// <summary>
    /// Every reason the value was refused. Empty when the exception was constructed without detail, so
    /// a handler that reports these must cope with an empty list.
    /// </summary>
    public IReadOnlyList<ValidationError> Errors { get; }

    private static string Describe(Type objectType, IReadOnlyList<ValidationError>? errors)
    {
        ArgumentNullException.ThrowIfNull(objectType);

        var message = $"The value object {objectType.Name} is invalid.";

        return errors is null || errors.Count == 0
            ? message
            : message + " " + string.Join(" ", errors.Select(error => error.ToString()));
    }
}
