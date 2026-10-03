using DDDToolkit.Exceptions;
using DDDToolkit.Validation;
using HotChocolate.Resolvers;

namespace DDDToolkit.HotChocolate.Errors;

/// <summary>
/// A value that broke its own rules, as one error in the mutation's payload that lists every reason:
/// <code>
/// type InvalidValuesError implements CodedError {
///   code: String!                        # always "invalid-value"
///   message: String!
///   arguments: [FailureArgument!]!       # always empty: each failure has its own
///   failures: [ValueFailure!]!
/// }
/// </code>
/// <para>
/// One error rather than one per reason, because a payload's <c>errors</c> says what became of the command, and
/// one thing became of it: it was not accepted. What a form shows is in <see cref="Failures"/>, each with the
/// input it belongs to.
/// </para>
/// <para>
/// The message is the exception's own, or the text the application's <c>IFailureLocalizer</c> keeps under
/// <see cref="InvalidValue"/>, looked up as a refusal's code is. The value that was rejected is never sent
/// back: it may be a password.
/// </para>
/// </summary>
public sealed class InvalidValuesError : ICodedError
{
    /// <summary>The code of every <see cref="InvalidValuesError"/>.</summary>
    public const string InvalidValue = "invalid-value";

    private readonly InvalidValueObjectException _invalid;

    private InvalidValuesError(InvalidValueObjectException invalid)
    {
        _invalid = invalid;
        Failures = [.. FailureValues.Failures(invalid).Select(static failure => new ValueFailure(failure))];
    }

    /// <summary>The factory HotChocolate's mutation conventions look for: the error an invalid value becomes.</summary>
    /// <param name="exception">The exception a mutation's resolver threw.</param>
    public static InvalidValuesError CreateErrorFrom(InvalidValueObjectException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new InvalidValuesError(exception);
    }

    /// <summary>Always <see cref="InvalidValue"/>; each of the <see cref="Failures"/> has the code of its own rule.</summary>
    public string Code => InvalidValue;

    /// <summary>Always empty: the values are those of each failure.</summary>
    public IReadOnlyList<FailureArgument> Arguments => [];

    /// <summary>
    /// Every reason the value was refused, never empty: an exception thrown without detail still gives one
    /// failure, with the code <see cref="ValidationError.UnspecifiedCode"/>.
    /// </summary>
    public IReadOnlyList<ValueFailure> Failures { get; }

    /// <inheritdoc />
    public string GetMessage(IResolverContext context)
        => FailureValues.UnderCode(context, InvalidValue, RefusalKind.Invalid, _invalid.Message);
}

/// <summary>
/// One reason a value was refused, in an <see cref="InvalidValuesError"/>:
/// <code>
/// type ValueFailure {
///   code: String!
///   message: String!
///   field: String
///   arguments: [FailureArgument!]!
/// }
/// </code>
/// </summary>
public sealed class ValueFailure
{
    private readonly ValidationError _failure;

    internal ValueFailure(ValidationError failure)
    {
        _failure = failure;
        Arguments = FailureValues.AsList(failure.Arguments);
    }

    /// <summary>The rule's code, or <see cref="ValidationError.UnspecifiedCode"/> when the rule gave none.</summary>
    public string Code => _failure.Code ?? ValidationError.UnspecifiedCode;

    /// <summary>The property the failure belongs to, or <see langword="null"/> when it is about the whole value.</summary>
    public string? Field => string.IsNullOrEmpty(_failure.PropertyName) ? null : _failure.PropertyName;

    /// <summary>The values the message was built from, by name.</summary>
    public IReadOnlyList<FailureArgument> Arguments { get; }

    /// <summary>
    /// The failure in the reader's language when the application registered an <c>IFailureLocalizer</c>, and
    /// in the domain's own words otherwise. The field <c>message</c>.
    /// </summary>
    /// <param name="context">The resolver context of the <c>message</c> field.</param>
    public string GetMessage(IResolverContext context)
        => FailureValues.Localizer(context)?.Localize(_failure) ?? _failure.Message;
}
