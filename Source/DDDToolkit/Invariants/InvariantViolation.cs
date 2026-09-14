namespace DDDToolkit.Invariants;

/// <summary>
/// One rule of an aggregate or entity that does not hold.
/// <para>
/// This is not a <see cref="DDDToolkit.Validation.ValidationError"/> and the difference is
/// deliberate. Validation says "this input is not acceptable" and belongs at the edge of the system;
/// an invariant violation says "this object is in a state the domain says cannot exist". The two
/// stay separate types so that code reading one is never handed the other.
/// </para>
/// <para>
/// Like everything else in the toolkit's failure model it is not a <c>Result&lt;T&gt;</c>: you are
/// handed the violations and you put them in whatever result type your application already uses.
/// </para>
/// </summary>
/// <param name="Code">
/// A stable machine-readable identifier for the rule, so a caller can branch on it without matching
/// on text and without the message becoming an API.
/// </param>
/// <param name="Message">What is wrong, in the domain's own words.</param>
/// <param name="EntityType">
/// The entity that reported it, so a violation found while walking an aggregate says which of its
/// children is the problem rather than only that something is.
/// </param>
/// <param name="EntityId">
/// The id of that entity, boxed. A violation is the failure path, so the box costs nothing that
/// matters, and a caller that has an id in hand can compare without parsing a message.
/// </param>
public sealed record InvariantViolation(string Code, string Message, Type? EntityType = null, object? EntityId = null)
{
    /// <summary>
    /// The code given to a violation that came from the <c>CheckInvariants()</c> seam rather than from
    /// an <see cref="IInvariant{TEntity}"/>. The seam throws a message and has nowhere to put a code,
    /// so every violation it reports carries this one. Branch on it to find rules that would read
    /// better as a named invariant.
    /// </summary>
    public const string SeamCode = "CheckInvariants";

    /// <summary>Reads as "OrderLine ORD_L_1 QUANTITY: a line must cost something".</summary>
    public override string ToString()
    {
        var subject = EntityType is null ? Code : EntityType.Name + " " + EntityId + " " + Code;
        return subject + ": " + Message;
    }
}
