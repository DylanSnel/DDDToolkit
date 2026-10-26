namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Marks a <c>static partial</c> method of an <see cref="AccessFunctionsAttribute"/> class as a question an
/// SQL function answers with one value, such as the caller's team:
/// <code>
/// [AccessScalar("caller_team")]
/// public static partial string CallerTeam();
///
/// // in a rule
/// =&gt; ticket.Team == Questions.CallerTeam();
/// </code>
/// Without an argument that reads the row, the policy asks it once per statement,
/// <c>(SELECT &lt;schema&gt;.caller_team())</c>, in the schema of the context that defines the function.
/// </summary>
/// <remarks>
/// The function answers <c>NULL</c> for a caller it does not know, and a comparison with its answer is SQL's
/// own, <c>"Team" = (SELECT &lt;schema&gt;.caller_team())</c>: null then, whether it is <c>==</c>, <c>!=</c> or
/// either one negated, which a policy counts as no. A caller the question knows nothing about never passes a
/// comparison built on it.
/// </remarks>
/// <param name="name">
/// The function's name: <c>schema.name</c>; <c>owner/name</c>; or <c>name</c>, relative to the class's
/// owner. See <see cref="AccessFunctionsAttribute"/>.
/// </param>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class AccessScalarAttribute(string name) : Attribute
{
    /// <summary>The function's name, as written: <c>schema.name</c>, <c>owner/name</c> or <c>name</c>.</summary>
    public string Name { get; } = name;
}
