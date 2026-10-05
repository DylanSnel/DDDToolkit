using DDDToolkit.Exceptions;
using HotChocolate.Resolvers;

namespace DDDToolkit.HotChocolate.Errors;

/// <summary>
/// A refused command, as an error in the mutation's payload: the refusal's code, its kind, its message and the
/// values the message was built from.
/// <code>
/// type RefusalError implements CodedError {
///   code: String!
///   message: String!
///   arguments: [FailureArgument!]!
///   kind: RefusalKind!
///   field: String
/// }
/// </code>
/// <para>
/// Made of a <see cref="RefusalException"/> and of nothing else: HotChocolate matches an error type by the
/// exception's exact type, so a class derived from <see cref="RefusalException"/> is not turned into this and
/// reaches the client as a top-level coded error instead, through <c>AddDDDToolkitErrors()</c>.
/// </para>
/// </summary>
[GraphQLDescription("A command that was refused before anything changed: the refusal's code, its kind, its message and the values the message was built from.")]
public sealed class RefusalError : ICodedError
{
    private readonly RefusalException _refusal;

    private RefusalError(RefusalException refusal)
    {
        _refusal = refusal;
        Arguments = FailureValues.AsList(refusal.Arguments);
    }

    /// <summary>The factory HotChocolate's mutation conventions look for: the error a refusal becomes.</summary>
    /// <param name="exception">The refusal a mutation's resolver threw.</param>
    public static RefusalError CreateErrorFrom(RefusalException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new RefusalError(exception);
    }

    /// <summary>The refusal's <see cref="RefusalException.Code"/>.</summary>
    [GraphQLDescription("The rule that refused, stable across releases and languages: what a client branches on.")]
    public string Code => _refusal.Code;

    /// <summary>Which of the four answers it is, so a client tells "not allowed" from "not found" without a status code.</summary>
    [GraphQLDescription("Which of the four answers it is, so a client tells \"not allowed\" from \"not found\" without a status code.")]
    public RefusalKind Kind => _refusal.Kind;

    /// <summary>
    /// The input the refusal is about, when it names one with <see cref="RefusalException.FieldArgument"/>:
    /// where a form puts the message. <see langword="null"/> for a refusal about the command as a whole.
    /// </summary>
    [GraphQLDescription("The input the refusal is about, where a form puts the message. Null for a refusal about the command as a whole.")]
    public string? Field => FailureValues.TextOf(_refusal.Arguments, RefusalException.FieldArgument);

    /// <inheritdoc />
    [GraphQLDescription(ErrorDescriptions.Arguments)]
    public IReadOnlyList<FailureArgument> Arguments { get; }

    /// <inheritdoc />
    [GraphQLDescription(ErrorDescriptions.Message)]
    public string GetMessage(IResolverContext context)
        => FailureValues.Localizer(context)?.Localize(_refusal) ?? _refusal.Message;
}
