using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;
using DDDToolkit.Localization;
using DDDToolkit.Validation;
using HotChocolate;
using HotChocolate.Execution;

namespace DDDToolkit.HotChocolate.Errors;

/// <summary>
/// Turns the toolkit's two failure exceptions into GraphQL errors a client can read: one error per
/// failure, each with the failure's code, in the reader's language.
/// <list type="bullet">
/// <item>
/// <see cref="InvalidValueObjectException"/> becomes one error per <see cref="ValidationError"/>, with
/// <c>field</c> set to its property.
/// </item>
/// <item>
/// <see cref="InvariantViolationException"/> becomes one error per <see cref="InvariantViolation"/>, with
/// <c>entity</c> and <c>entityId</c> set to whoever reported it, so a violation in a child says which one.
/// </item>
/// </list>
/// <para>
/// Every error carries <c>code</c> and <c>arguments</c> in its extensions, so a client branches on the
/// code rather than on the message. The value that was rejected is never repeated back: it may be a
/// password.
/// </para>
/// <para>
/// The message comes from the <see cref="IFailureLocalizer"/> when the application registered one, and is
/// the domain's own sentence otherwise. The language is the current UI culture, which ASP.NET Core's
/// request localization middleware sets per request.
/// </para>
/// <para>Any other error passes through untouched.</para>
/// </summary>
/// <param name="localizer">Phrases the failures, or <see langword="null"/> to keep the domain's own messages.</param>
public sealed class FailureErrorFilter(IFailureLocalizer? localizer = null) : IErrorFilter
{
    /// <summary>The extension naming the property a validation failure belongs to.</summary>
    public const string FieldExtension = "field";

    /// <summary>The extension naming the entity type that reported a violation.</summary>
    public const string EntityExtension = "entity";

    /// <summary>The extension holding the id of the entity that reported a violation.</summary>
    public const string EntityIdExtension = "entityId";

    /// <summary>The extension holding the values the message was built from.</summary>
    public const string ArgumentsExtension = "arguments";

    /// <inheritdoc />
    public IError OnError(IError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error.Exception switch
        {
            InvalidValueObjectException invalid => Split(error, Failures(invalid).Select(failure => FromValidation(error, failure))),
            InvariantViolationException broken => Split(error, Violations(broken).Select(violation => FromViolation(error, violation))),
            _ => error,
        };
    }

    /// <summary>
    /// The failures behind the exception. One constructed without detail still gets the toolkit's own
    /// "is not valid" failure, so the client is never handed an error with no code.
    /// </summary>
    private static IEnumerable<ValidationError> Failures(InvalidValueObjectException invalid)
        => invalid.Errors.Count > 0
            ? invalid.Errors
            : [new ValidationError(invalid.Message, code: ValidationError.UnspecifiedCode)
                .With(ValidationError.ValueObjectArgument, invalid.ObjectType.Name)];

    /// <summary>The violations behind the exception, or one carrying its message when it was built from a message alone.</summary>
    private static IEnumerable<InvariantViolation> Violations(InvariantViolationException broken)
        => broken.InvariantViolations.Count > 0
            ? broken.InvariantViolations
            : [new InvariantViolation(InvariantViolation.SeamCode, broken.Message, broken.AggregateType, broken.AggregateId)];

    private IError FromValidation(IError error, ValidationError failure)
    {
        var builder = ErrorBuilder.FromError(error)
            .SetMessage(localizer?.Localize(failure) ?? failure.Message)
            .SetCode(failure.Code ?? ValidationError.UnspecifiedCode)
            .SetExtension(ArgumentsExtension, Arguments(failure.Arguments));

        if (!string.IsNullOrEmpty(failure.PropertyName))
        {
            builder.SetExtension(FieldExtension, failure.PropertyName);
        }

        return builder.Build();
    }

    private IError FromViolation(IError error, InvariantViolation violation)
    {
        var builder = ErrorBuilder.FromError(error)
            .SetMessage(localizer?.Localize(violation) ?? violation.Message)
            .SetCode(violation.Code)
            .SetExtension(ArgumentsExtension, Arguments(violation.Arguments));

        if (violation.EntityType is not null)
        {
            builder.SetExtension(EntityExtension, violation.EntityType.Name);
        }

        if (violation.EntityId is not null)
        {
            builder.SetExtension(EntityIdExtension, violation.EntityId.ToString());
        }

        return builder.Build();
    }

    /// <summary>One error stays one error; several become an <see cref="AggregateError"/>, which HotChocolate reports one by one.</summary>
    private static IError Split(IError error, IEnumerable<IError> errors)
    {
        var all = errors.ToArray();
        return all.Length switch
        {
            0 => error,
            1 => all[0],
            _ => new AggregateError(all),
        };
    }

    /// <summary>
    /// The arguments as values any GraphQL serializer can write: strings, booleans and numbers as they
    /// are, anything else (an id, a type, a value object) as its text.
    /// </summary>
    private static Dictionary<string, object?> Arguments(IReadOnlyDictionary<string, object?> arguments)
    {
        var result = new Dictionary<string, object?>(arguments.Count, StringComparer.Ordinal);
        foreach (var (name, value) in arguments)
        {
            result[name] = value switch
            {
                null or string or bool => value,
                byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
                Type type => type.Name,
                _ => value.ToString(),
            };
        }

        return result;
    }
}
