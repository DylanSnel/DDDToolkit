namespace DTS.DDD.Abstractions.Attributes;

/// <summary>
/// Gives an error record its code and its English message. The generator overrides <c>Code</c>, <c>Message</c> and
/// <c>Arguments</c> of the record, so the record declares only its data:
/// <code>
/// [ErrorCode("stock.out-of-stock", "Only {Available} left of {Sku}.")]
/// public sealed partial record OutOfStock(string Sku, int Available) : ConflictError;
/// </code>
/// <para>
/// The record is a <c>partial record</c> that derives from <c>DTS.DDD.Results.Error</c>, directly or through one of
/// the five kinds. Each placeholder in the message, such as <c>{Available}</c>, names a property of the record. The
/// generator fills it in with the invariant culture. A placeholder may carry a format, such as <c>{Price:0.00}</c>.
/// </para>
/// <para>
/// <c>Arguments</c> holds every public instance property that the record declares, by its name. A localizer fills
/// its translated template from them.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ErrorCodeAttribute : Attribute
{
    /// <param name="code">The stable code, such as <c>stock.out-of-stock</c>.</param>
    /// <param name="message">The English message, with a placeholder for each property it shows.</param>
    public ErrorCodeAttribute(string code, string message)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("An error code cannot be empty.", nameof(code));
        }

        Code = code;
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    /// <summary>The stable code.</summary>
    public string Code { get; }

    /// <summary>The English message with its placeholders.</summary>
    public string Message { get; }
}
