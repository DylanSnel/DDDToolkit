namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Marks a question row access rules ask about <typeparamref name="TAggregate"/> that reads its entities,
/// written once as C# and made one SQL function in the database, <paramref name="name"/>. Apply to a
/// <c>static partial class</c> shaped like a rule:
/// <code>
/// [AccessFunction&lt;Project&gt;("projects.is_member")]
/// public static partial class ProjectMembership
/// {
///     public static bool Allows(Project project, Caller caller)
///         =&gt; project.Members.Any(member =&gt; member.UserId == caller.UserId);
/// }
///
/// [RowAccess&lt;Project&gt;(RowOperations.Read)]
/// public static partial class MembersSeeTheirProjects
/// {
///     public static bool Allows(Project project, Caller caller) =&gt; ProjectMembership.Allows(project, caller);
/// }
/// </code>
/// </summary>
/// <remarks>
/// A policy on the aggregate's table cannot read the tables of its entities itself: their policies ask the
/// aggregate's table back, and Postgres stops with infinite recursion. The function runs as its owner,
/// <c>SECURITY DEFINER</c>, so it reads them without their policies, and a rule calls it with the row's id.
/// The context that maps <typeparamref name="TAggregate"/> writes it, once, however many modules see this
/// class; another module calls it by name with <c>Sql.Call&lt;bool&gt;("projects.is_member", task.ProjectId)</c>.
/// The method stays an ordinary method, so C# asks the same question of an aggregate it holds.
/// </remarks>
/// <typeparam name="TAggregate">The aggregate root the question is about.</typeparam>
/// <param name="name">The function's name with its schema, <c>projects.is_member</c>.</param>
#pragma warning disable S2326 // Unused type parameters should be removed (read by the source generator).
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AccessFunctionAttribute<TAggregate>(string name) : Attribute
#pragma warning restore S2326
{
    /// <summary>The function's name with its schema, <c>projects.is_member</c>.</summary>
    public string Name { get; } = name;
}
