using DDDToolkit.Exceptions;
using HotChocolate.Resolvers;

namespace DDDToolkit.HotChocolate.Errors;

/// <summary>
/// A change that lost a race, as an error in the mutation's payload: somebody else changed the same aggregate
/// after this caller read it, or the version the caller expected is not the one that is stored.
/// <code>
/// type ConcurrencyConflictError implements CodedError {
///   code: String!                        # always "concurrency-conflict"
///   message: String!
///   arguments: [FailureArgument!]!       # always empty
/// }
/// </code>
/// <para>
/// One answer for both, because a client does the same thing for both: read again, and decide again. The
/// message is the exception's own, or the text the application's <c>IFailureLocalizer</c> keeps under
/// <see cref="ConcurrencyConflict"/>, looked up as a refusal's code is.
/// </para>
/// </summary>
public sealed class ConcurrencyConflictError : ICodedError
{
    /// <summary>The code of every <see cref="ConcurrencyConflictError"/>.</summary>
    public const string ConcurrencyConflict = "concurrency-conflict";

    private readonly ConcurrencyConflictException _conflict;

    private ConcurrencyConflictError(ConcurrencyConflictException conflict) => _conflict = conflict;

    /// <summary>The factory HotChocolate's mutation conventions look for: the error a lost race becomes.</summary>
    /// <param name="exception">The exception a mutation's resolver threw.</param>
    public static ConcurrencyConflictError CreateErrorFrom(ConcurrencyConflictException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new ConcurrencyConflictError(exception);
    }

    /// <summary>Always <see cref="ConcurrencyConflict"/>.</summary>
    public string Code => ConcurrencyConflict;

    /// <summary>Always empty.</summary>
    public IReadOnlyList<FailureArgument> Arguments => [];

    /// <inheritdoc />
    public string GetMessage(IResolverContext context)
        => FailureValues.UnderCode(context, ConcurrencyConflict, RefusalKind.Conflict, _conflict.Message);
}
