using DTS.DDD.Validation;

namespace DTS.DDD.Results;

/// <summary>
/// Why a <see cref="Result"/> or <see cref="Result{T}"/> failed: a stable code that a client branches on, a message,
/// and the values the message was built from.
/// </summary>
/// <remarks>
/// <para>
/// The toolkit has five kinds, one record each: <see cref="InvalidError"/>, <see cref="NotPermittedError"/>,
/// <see cref="NotFoundError"/>, <see cref="ConflictError"/> and <see cref="UnexpectedError"/>. The GraphQL and HTTP
/// edges, the logging and the retries read the kind from the type. An application derives its own errors from one of
/// the five, such as a rate limit from <see cref="ConflictError"/>, and adds the data that error carries.
/// </para>
/// <para>
/// <see cref="Arguments"/> holds the values by name, such as <c>MaxLength</c>. A localizer looks the error up by
/// <see cref="Code"/> and fills a template from them, as it does for a <see cref="Exceptions.RefusalException"/>.
/// The argument <see cref="FieldArgument"/> names the input the error is about.
/// </para>
/// </remarks>
public abstract record Error
{
    /// <summary>The argument that names the input an error is about, such as <c>ownerEmail</c>.</summary>
    public const string FieldArgument = "Field";

    /// <param name="code">The stable code, such as <c>onsite.owner-has-account</c>.</param>
    /// <param name="message">The message in English, with the values filled in.</param>
    /// <param name="arguments">The values the message was built from, by name.</param>
    /// <exception cref="ArgumentException"><paramref name="code"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    protected Error(string code, string message, IEnumerable<KeyValuePair<string, object?>>? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(message);
        Code = code;
        Message = message;
        Arguments = FailureArguments.Copy(arguments);
    }

    /// <summary>The stable code, the same in every release and language.</summary>
    public string Code { get; }

    /// <summary>The message in English, with the values filled in.</summary>
    public string Message { get; }

    /// <summary>The values the message was built from, by name, matched without regard to case. Never null.</summary>
    public IReadOnlyDictionary<string, object?> Arguments { get; }

    /// <summary>The input the error is about, from <see cref="FieldArgument"/>, or null for the request as a whole.</summary>
    public string? Field => Arguments.TryGetValue(FieldArgument, out var input) ? input?.ToString() : null;

    /// <summary>Turns the error into an exception, for code that has no result to return it in.</summary>
    public ErrorException ToException() => new(this);

    /// <inheritdoc />
    public override string ToString() => $"{GetType().Name} {Code}: {Message}";

    /// <inheritdoc />
    public virtual bool Equals(Error? other)
        => other is not null
           && EqualityContract == other.EqualityContract
           && Code == other.Code
           && Message == other.Message
           && FailureArguments.AreEqual(Arguments, other.Arguments);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(EqualityContract, Code, Message, FailureArguments.GetHashCode(Arguments));
}

/// <summary>
/// The request is not valid: a value is missing, malformed or out of range. HTTP 400. <see cref="Failures"/> holds one
/// failure per input, so a form shows all of them at once.
/// </summary>
public record InvalidError : Error
{
    /// <param name="code">The stable code.</param>
    /// <param name="message">The message in English.</param>
    /// <param name="failures">One failure per input. Empty for an error about the request as a whole.</param>
    /// <param name="arguments">The values the message was built from.</param>
    public InvalidError(string code, string message, IEnumerable<ValidationError>? failures = null, IEnumerable<KeyValuePair<string, object?>>? arguments = null)
        : base(code, message, arguments)
        => Failures = [.. failures ?? []];

    /// <summary>One failure per input, each with its property, code and arguments. Never null.</summary>
    public IReadOnlyList<ValidationError> Failures { get; }

    /// <summary>An error about one input, such as <c>InvalidError.ForField("owner-required", "...", "ownerEmail")</c>.</summary>
    public static InvalidError ForField(string code, string message, string field, IEnumerable<KeyValuePair<string, object?>>? arguments = null)
        => new(code, message, arguments: FailureArguments.With(FailureArguments.Copy(arguments), FieldArgument, field));

    /// <inheritdoc />
    public virtual bool Equals(InvalidError? other) => base.Equals(other) && Failures.SequenceEqual(other!.Failures);

    /// <inheritdoc />
    public override int GetHashCode() => base.GetHashCode();
}

/// <summary>The caller may not do this: no seat, no permission, or the wrong tenant. HTTP 403.</summary>
public record NotPermittedError : Error
{
    /// <param name="code">The stable code.</param>
    /// <param name="message">The message in English.</param>
    /// <param name="arguments">The values the message was built from.</param>
    public NotPermittedError(string code, string message, IEnumerable<KeyValuePair<string, object?>>? arguments = null)
        : base(code, message, arguments)
    {
    }
}

/// <summary>What the request names does not exist, or the caller may not see it. HTTP 404.</summary>
public record NotFoundError : Error
{
    /// <param name="code">The stable code.</param>
    /// <param name="message">The message in English.</param>
    /// <param name="arguments">The values the message was built from.</param>
    public NotFoundError(string code, string message, IEnumerable<KeyValuePair<string, object?>>? arguments = null)
        : base(code, message, arguments)
    {
    }
}

/// <summary>The request conflicts with the current state, such as a slug that is taken. HTTP 409.</summary>
public record ConflictError : Error
{
    /// <param name="code">The stable code.</param>
    /// <param name="message">The message in English.</param>
    /// <param name="arguments">The values the message was built from.</param>
    public ConflictError(string code, string message, IEnumerable<KeyValuePair<string, object?>>? arguments = null)
        : base(code, message, arguments)
    {
    }
}

/// <summary>
/// Something outside the request failed, such as another service or the database. HTTP 500. The only kind that the
/// logging writes as an error and that a retry may help.
/// </summary>
public record UnexpectedError : Error
{
    /// <param name="code">The stable code.</param>
    /// <param name="message">The message in English.</param>
    /// <param name="arguments">The values the message was built from.</param>
    public UnexpectedError(string code, string message, IEnumerable<KeyValuePair<string, object?>>? arguments = null)
        : base(code, message, arguments)
    {
    }
}