using DTS.DDD.Exceptions;
using DTS.DDD.Validation;

namespace DTS.DDD.Results;

/// <summary>The errors the toolkit makes from its exceptions, so code that throws and code that returns a result meet.</summary>
public static class Errors
{
    /// <summary>The code of the error made from an <see cref="InvalidValueObjectException"/>.</summary>
    public const string ValueInvalidCode = ValidationError.UnspecifiedCode;

    /// <summary>
    /// The error <paramref name="exception"/> stands for, or null for an exception that is no refusal: a
    /// <see cref="RefusalException"/> by its kind, an <see cref="InvalidValueObjectException"/> as an
    /// <see cref="InvalidError"/> with its failures, and the error of an <see cref="ErrorException"/>.
    /// </summary>
    public static Error? FromException(Exception exception) => exception switch
    {
        ErrorException wrapped => wrapped.Error,
        RefusalException refusal => FromRefusal(refusal),
        InvalidValueObjectException invalid => FromInvalidValueObject(invalid),
        _ => null,
    };

    /// <summary>The error of a refusal: its kind decides the type, and the code, message and arguments stay.</summary>
    public static Error FromRefusal(RefusalException refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return refusal.Kind switch
        {
            RefusalKind.Invalid => new InvalidError(refusal.Code, refusal.Message, arguments: refusal.Arguments),
            RefusalKind.NotPermitted => new NotPermittedError(refusal.Code, refusal.Message, refusal.Arguments),
            RefusalKind.NotFound => new NotFoundError(refusal.Code, refusal.Message, refusal.Arguments),
            RefusalKind.Conflict => new ConflictError(refusal.Code, refusal.Message, refusal.Arguments),
            _ => new UnexpectedError(refusal.Code, refusal.Message, refusal.Arguments),
        };
    }

    /// <summary>
    /// The error of an invalid value object: <see cref="ValueInvalidCode"/>, with the name of the value object as the
    /// argument <see cref="ValidationError.ValueObjectArgument"/> and its failures.
    /// </summary>
    public static InvalidError FromInvalidValueObject(InvalidValueObjectException invalid)
    {
        ArgumentNullException.ThrowIfNull(invalid);
        return new InvalidError(
            ValueInvalidCode,
            invalid.Message,
            invalid.Errors,
            [new(ValidationError.ValueObjectArgument, invalid.ObjectType.Name)]);
    }
}