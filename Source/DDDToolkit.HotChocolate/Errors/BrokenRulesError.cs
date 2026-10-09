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
[GraphQLDescription("An aggregate that would have broken its own rules: one error for the command, with every rule that was broken in its violations. Its code, message and arguments are those of the first.")]
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
    [GraphQLDescription("The code of the first rule that was broken.")]
    public string Code => _first.Code;

    /// <summary>The values the first violation's message was built from.</summary>
    [GraphQLDescription("The values the first violation's message was built from.")]
    public IReadOnlyList<FailureArgument> Arguments { get; }

    /// <summary>
    /// Every rule that was broken, in the order the aggregate reported them, never empty: an exception built
    /// from a message alone still gives one violation, with the code <see cref="InvariantViolation.SeamCode"/>.
    /// </summary>
    [GraphQLDescription("Every rule that was broken, in the order the aggregate reported them, never empty.")]
    public IReadOnlyList<RuleViolation> Violations { get; }

    /// <inheritdoc />
    [GraphQLDescription("The first rule that was broken, in the reader's language when the server has a translation for it, and in the domain's own words otherwise.")]
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
[GraphQLDescription("One broken rule, in a BrokenRulesError. A violation in a child entity says which child it was.")]
public sealed class RuleViolation
{
    private readonly InvariantViolation _violation;

    internal RuleViolation(InvariantViolation violation)
    {
        _violation = violation;
        Arguments = FailureValues.AsList(violation.Arguments);
    }

    /// <summary>The rule's code.</summary>
    [GraphQLDescription("The rule's code.")]
    public string Code => _violation.Code;

    /// <summary>The type of the entity that reported the violation, when it said so.</summary>
    [GraphQLDescription("The type of the entity that reported the violation, when it said so.")]
    public string? Entity => _violation.EntityType?.Name;

    /// <summary>The id of the entity that reported the violation, as text, so a violation in a child says which one.</summary>
    [GraphQLDescription("The id of the entity that reported the violation, as text, so a violation in a child says which one.")]
    public string? EntityId => _violation.EntityId?.ToString();

    /// <summary>The values the message was built from, by name.</summary>
    [GraphQLDescription(ErrorDescriptions.Arguments)]
    public IReadOnlyList<FailureArgument> Arguments { get; }

    /// <summary>
    /// The violation in the reader's language when the application registered an <c>IFailureLocalizer</c>,
    /// and in the domain's own words otherwise. The field <c>message</c>.
    /// </summary>
    /// <param name="context">The resolver context of the <c>message</c> field.</param>
    [GraphQLDescription(ErrorDescriptions.Message)]
    public string GetMessage(IResolverContext context)
        => FailureValues.Localizer(context)?.Localize(_violation) ?? _violation.Message;
}
