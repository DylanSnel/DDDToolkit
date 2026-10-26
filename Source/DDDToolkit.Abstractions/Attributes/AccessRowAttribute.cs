namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Marks a <c>static partial</c> method of an <see cref="AccessFunctionsAttribute"/> class as a question an SQL
/// function answers with one row of a table the context maps, such as the caller's own seat, whose properties a rule
/// reads as it reads its own row's:
/// <code>
/// [AccessRow("caller_seat_row")]
/// public static partial TSeat CallerSeat&lt;TSeat&gt;() where TSeat : class;
///
/// // in a rule
/// =&gt; TenancyRowAccess.CallerSeat&lt;Seat&gt;().Kind == SeatKind.Employee;
/// </code>
/// The method returns the class the table is mapped to, or a type parameter the rule names it with. A property of
/// the answer is <c>(SELECT r1."Kind" FROM &lt;schema&gt;.caller_seat_row() r1)</c> in the policy: its column is the
/// one the model maps it to, and an enum or a value object compared with it is written as that column stores it, as
/// for the rule's own row. The function returns the table's rows, <c>SETOF</c> the table, and is asked once per
/// statement.
/// </summary>
/// <remarks>
/// The function answers no row for a caller it does not know, so a property of its answer is <c>NULL</c> then, and a
/// comparison with it is SQL's own, as with an <see cref="AccessScalarAttribute"/> question: such a caller never passes
/// it, negated or not. A function that reads the very table a rule guards runs as its owner, so the table's policies
/// do not ask themselves.
/// </remarks>
/// <param name="name">
/// The function's name: <c>schema.name</c>; <c>owner/name</c>; or <c>name</c>, relative to the class's
/// owner. See <see cref="AccessFunctionsAttribute"/>.
/// </param>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class AccessRowAttribute(string name) : Attribute
{
    /// <summary>The function's name, as written: <c>schema.name</c>, <c>owner/name</c> or <c>name</c>.</summary>
    public string Name { get; } = name;
}
