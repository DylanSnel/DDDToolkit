using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;
using DDDToolkit.Localization;
using DDDToolkit.Validation;
using HotChocolate;
using HotChocolate.Execution;

namespace DDDToolkit.HotChocolate.Errors;

/// <summary>
/// Turns the toolkit's failure exceptions into GraphQL errors a client can read: one error per failure,
/// each with the failure's code, in the reader's language.
/// <list type="bullet">
/// <item>
/// <see cref="InvalidValueObjectException"/> becomes one error per <see cref="ValidationError"/>, with
/// <c>field</c> set to its property.
/// </item>
/// <item>
/// <see cref="InvariantViolationException"/> becomes one error per <see cref="InvariantViolation"/>, with
/// <c>entity</c> and <c>entityId</c> set to whoever reported it, so a violation in a child says which one.
/// </item>
/// <item>
/// <see cref="RefusalException"/> becomes one error with its code, and <c>kind</c> set to its
/// <see cref="RefusalKind"/>, so a client can tell "not allowed" from "not found" without a status code. A
/// refusal that names the input it is about, with <see cref="RefusalException.FieldArgument"/>, has
/// <c>field</c> set to it as well, so a form puts the message where it puts a validation failure's.
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
public sealed class FailureErrorFilter : IErrorFilter
{
    private readonly IFailureLocalizer? _localizer;
    private readonly EnumValueSpelling? _kindSpelling;

    /// <summary>Creates the filter. A refusal's <c>kind</c> is written as the enum's member is named: <c>NotPermitted</c>.</summary>
    /// <param name="localizer">Phrases the failures, or <see langword="null"/> to keep the domain's own messages.</param>
    public FailureErrorFilter(IFailureLocalizer? localizer = null) => _localizer = localizer;

    /// <summary>
    /// Creates the filter for a schema whose enum values are spelled with
    /// <see cref="DependencyInjection.AddDDDToolkitEnumValues"/>: a refusal's <c>kind</c> is spelled the same
    /// way, <c>not_permitted</c> or <c>NOT_PERMITTED</c>, so a client reads one spelling of
    /// <see cref="RefusalKind"/> whether it arrives in an error's extensions or in a mutation's payload.
    /// </summary>
    /// <param name="localizer">Phrases the failures, or <see langword="null"/> to keep the domain's own messages.</param>
    /// <param name="kindSpelling">How a refusal's <c>kind</c> is spelled.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kindSpelling"/> is not one of the two spellings.</exception>
    public FailureErrorFilter(IFailureLocalizer? localizer, EnumValueSpelling kindSpelling)
    {
        _localizer = localizer;
        _kindSpelling = EnumValueSpellings.Checked(kindSpelling, nameof(kindSpelling));
    }

    /// <summary>
    /// The extension naming the property a validation failure belongs to, and the input a refusal is about when
    /// it names one (<see cref="RefusalException.FieldArgument"/>).
    /// </summary>
    public const string FieldExtension = "field";

    /// <summary>The extension naming the entity type that reported a violation.</summary>
    public const string EntityExtension = "entity";

    /// <summary>The extension holding the id of the entity that reported a violation.</summary>
    public const string EntityIdExtension = "entityId";

    /// <summary>The extension holding the values the message was built from.</summary>
    public const string ArgumentsExtension = "arguments";

    /// <summary>The extension naming a refusal's <see cref="RefusalKind"/>.</summary>
    public const string KindExtension = "kind";

    /// <inheritdoc />
    public IError OnError(IError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error.Exception switch
        {
            InvalidValueObjectException invalid => Split(error, FailureValues.Failures(invalid).Select(failure => FromValidation(error, failure))),
            InvariantViolationException broken => Split(error, FailureValues.Violations(broken).Select(violation => FromViolation(error, violation))),
            RefusalException refusal => FromRefusal(error, refusal),
            _ => error,
        };
    }

    private IError FromValidation(IError error, ValidationError failure)
    {
        var builder = ErrorBuilder.FromError(error)
            .SetMessage(_localizer?.Localize(failure) ?? failure.Message)
            .SetCode(failure.Code ?? ValidationError.UnspecifiedCode)
            .SetExtension(ArgumentsExtension, FailureValues.AsExtension(failure.Arguments));

        if (!string.IsNullOrEmpty(failure.PropertyName))
        {
            builder.SetExtension(FieldExtension, failure.PropertyName);
        }

        return builder.Build();
    }

    private IError FromViolation(IError error, InvariantViolation violation)
    {
        var builder = ErrorBuilder.FromError(error)
            .SetMessage(_localizer?.Localize(violation) ?? violation.Message)
            .SetCode(violation.Code)
            .SetExtension(ArgumentsExtension, FailureValues.AsExtension(violation.Arguments));

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

    private IError FromRefusal(IError error, RefusalException refusal)
    {
        var builder = ErrorBuilder.FromError(error)
            .SetMessage(_localizer?.Localize(refusal) ?? refusal.Message)
            .SetCode(refusal.Code)
            .SetExtension(KindExtension, Kind(refusal.Kind))
            .SetExtension(ArgumentsExtension, FailureValues.AsExtension(refusal.Arguments));

        // The input the refusal is about, where it names one: what RefusalError.field answers in a mutation's
        // payload, and where a validation failure names its property, so a client reads one place either way.
        if (FailureValues.TextOf(refusal.Arguments, RefusalException.FieldArgument) is { Length: > 0 } field)
        {
            builder.SetExtension(FieldExtension, field);
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

    /// <summary>The kind as the enum's member is named, or as the schema spells its enum values when the filter was told how.</summary>
    private string Kind(RefusalKind kind)
        => _kindSpelling is { } spelling ? EnumValueSpellings.Spell(kind.ToString(), spelling) : kind.ToString();
}
