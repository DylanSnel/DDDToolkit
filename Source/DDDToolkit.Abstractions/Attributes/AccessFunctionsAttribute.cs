namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Marks a <c>static partial class</c> of questions only the database can answer, which row access rules and
/// access functions ask: its <see cref="AccessSetAttribute"/> and <see cref="AccessScalarAttribute"/> methods.
/// Each is declared without a body, and the generator writes one that throws
/// <see cref="Access.DatabaseOnlyException"/>, because the answer is an SQL function's:
/// <code>
/// [AccessFunctions(Owner = "desk")]
/// public static partial class DeskQuestions
/// {
///     [AccessSet("tickets_i_watch")]
///     public static partial AccessSet&lt;TicketId&gt; TicketsIWatch();
///
///     [AccessScalar("caller_team")]
///     public static partial string CallerTeam();
/// }
/// </code>
/// </summary>
/// <remarks>
/// A question's name without a schema is relative to its owner: <c>tickets_i_watch</c> above is
/// <c>desk/tickets_i_watch</c>, and the export writes it as the function of that name in the schema of the
/// context that defines it, whatever the host calls that schema. The owner is <see cref="Owner"/>, or else
/// the name the declaring assembly gives its module with <c>[assembly: Module]</c>. Put the attribute on an
/// <c>[AccessFunction]</c> or an <c>[AccessFunctionContract]</c> class as well, to name the owner of its
/// function where the assembly declares no module, as a package does.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AccessFunctionsAttribute : Attribute
{
    /// <summary>
    /// The owner of the functions this class names without a schema: lower case letters, digits and
    /// <c>-</c>, as a module's name is written in a migration's file name. Needed where the declaring assembly
    /// has no <c>[assembly: Module]</c>, and it takes that module's place where it has one.
    /// </summary>
    public string? Owner { get; set; }
}
