using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;
using HotChocolate.Resolvers;

namespace DDDToolkit.HotChocolate.Errors;

/// <summary>
/// An aggregate that would have broken its own rules, as one error in the mutation's payload that lists every
/// rule:
/// <code>
/// type BrokenRulesError implements CodedError {
///   code: String!                        # the first violation's
///   message: String!                     # the first violation's
///   arguments: [FailureArgument!]!       # the first violation's
///   violations: [RuleViolation!]!
/// }
/// </code>
/// <para>
/// A client that shows one line shows the first rule that was broken; one that shows them all reads
/// <see cref="Violations"/>, where a violation in a child says which child it was.
/// </para>
/// </summary>
public sealed class BrokenRulesError : ICodedError
{
    private readonly InvariantViolation _first;

    private BrokenRulesError(InvariantViolationException broken)
    {
        var violations = FailureValues.Violations(broken);
        _first = violations[0];
        Violations = [.. violations.Select(static violation => new RuleViolation(violation))];
        Arguments = Violations[0].Arguments;
    }

    /// <summary>The factory HotChocolate's mutation conventions look for: the error a broken invariant becomes.</summary>
    /// <param name="exception">The exception a mutation's resolver threw.</param>
    public static BrokenRulesError CreateErrorFrom(InvariantViolationException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new BrokenRulesError(exception);
    }

    /// <summary>The code of the first rule that was broken.</summary>
    public string Code => _first.Code;

    /// <summary>The values the first violation's message was built from.</summary>
    public IReadOnlyList<FailureArgument> Arguments { get; }

    /// <summary>
    /// Every rule that was broken, in the order the aggregate reported them, never empty: an exception built
    /// from a message alone still gives one violation, with the code <see cref="InvariantViolation.SeamCode"/>.
    /// </summary>
    public IReadOnlyList<RuleViolation> Violations { get; }

    /// <inheritdoc />
    public string GetMessage(IResolverContext context)
        => FailureValues.Localizer(context)?.Localize(_first) ?? _first.Message;
}

/// <summary>
/// One broken rule, in a <see cref="BrokenRulesError"/>:
/// <code>
/// type RuleViolation {
///   code: String!
///   message: String!
///   entity: String
///   entityId: String
///   arguments: [FailureArgument!]!
/// }
/// </code>
/// </summary>
public sealed class RuleViolation
{
    private readonly InvariantViolation _violation;

    internal RuleViolation(InvariantViolation violation)
    {
        _violation = violation;
        Arguments = FailureValues.AsList(violation.Arguments);
    }

    /// <summary>The rule's code.</summary>
    public string Code => _violation.Code;

    /// <summary>The type of the entity that reported the violation, when it said so.</summary>
    public string? Entity => _violation.EntityType?.Name;

    /// <summary>The id of the entity that reported the violation, as text, so a violation in a child says which one.</summary>
    public string? EntityId => _violation.EntityId?.ToString();

    /// <summary>The values the message was built from, by name.</summary>
    public IReadOnlyList<FailureArgument> Arguments { get; }

    /// <summary>
    /// The violation in the reader's language when the application registered an <c>IFailureLocalizer</c>,
    /// and in the domain's own words otherwise. The field <c>message</c>.
    /// </summary>
    /// <param name="context">The resolver context of the <c>message</c> field.</param>
    public string GetMessage(IResolverContext context)
        => FailureValues.Localizer(context)?.Localize(_violation) ?? _violation.Message;
}
