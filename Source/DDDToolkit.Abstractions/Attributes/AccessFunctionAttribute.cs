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
/// <para>
/// A policy on the aggregate's table cannot read the tables of its entities itself: their policies ask the
/// aggregate's table back, and Postgres stops with infinite recursion. The function runs as its owner,
/// <c>SECURITY DEFINER</c>, so it reads them without their policies, and a rule calls it with the row's id.
/// The context that maps <typeparamref name="TAggregate"/> writes it, once, however many modules see this
/// class. The generator adds <c>Name</c>, and an <c>Allows(key)</c> a rule calls with a key rather than an
/// aggregate, <c>ProjectMembership.Allows(task.ProjectId)</c>, which only the database can answer. Another
/// module, which sees only this module's contracts, asks it through an
/// <see cref="AccessFunctionContractAttribute{TKey}"/> there. <c>Allows(project, caller)</c> stays an
/// ordinary method, so C# asks the same question of an aggregate it holds.
/// </para>
/// <para>
/// <c>Allows</c> may take more than the aggregate and the caller: strings, numbers, flags, <c>Guid</c>s and
/// ids, which the function takes as parameters after the key, and a rule passes along with it:
/// <c>ProjectMembership.Allows(task.ProjectId, "lead")</c>. With <see cref="Shape"/> set to
/// <see cref="AccessFunctionShape.Set"/>, the function answers with the keys of every aggregate it allows,
/// once per statement, and the generator adds <c>Ids(...)</c> in place of <c>Allows(key)</c>, for a rule to
/// ask <c>ProjectMembership.Ids().Contains(task.ProjectId)</c>.
/// </para>
/// </remarks>
/// <typeparam name="TAggregate">The aggregate root the question is about.</typeparam>
/// <param name="name">
/// The function's name: with its schema, <c>projects.is_member</c>; relative to the module,
/// <c>is_member</c>, which the export creates in the schema of the context that maps
/// <typeparamref name="TAggregate"/>; or as another module's contract names it, <c>projects/is_member</c>.
/// </param>
#pragma warning disable S2326 // Unused type parameters should be removed (read by the source generator).
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AccessFunctionAttribute<TAggregate>(string name) : Attribute
#pragma warning restore S2326
{
    /// <summary>The function's name, as written: <c>projects.is_member</c>, <c>is_member</c> or <c>projects/is_member</c>.</summary>
    public string Name { get; } = name;

    /// <summary>
    /// Whether the function answers about one aggregate, given its key (<see cref="AccessFunctionShape.Row"/>,
    /// the default), or with the keys of every aggregate it allows (<see cref="AccessFunctionShape.Set"/>).
    /// </summary>
    public AccessFunctionShape Shape { get; set; } = AccessFunctionShape.Row;
}
