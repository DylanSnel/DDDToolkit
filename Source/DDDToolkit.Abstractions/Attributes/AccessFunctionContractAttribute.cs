namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Publishes an access function a module defines, so the rules of other modules ask it by their
/// aggregate's key, typed, without the function's name in a string. Apply, in the module's contracts, to
/// an empty <c>static partial class</c>; the generator adds <c>Name</c> and <c>Allows(<typeparamref name="TKey"/>)</c>:
/// <code>
/// // Projects.Contracts
/// [AccessFunctionContract&lt;ProjectId&gt;("projects.is_member")]
/// public static partial class ProjectMembers;
///
/// // Projects: the definition, named after the contract
/// [AccessFunction&lt;Project&gt;(ProjectMembers.Name)]
/// public static partial class ProjectMembership
/// {
///     public static bool Allows(Project project, Caller caller)
///         =&gt; project.Members.Any(member =&gt; member.UserId == caller.UserId);
/// }
///
/// // Tasks
/// [RowAccess&lt;ProjectTask&gt;(RowOperations.All)]
/// public static partial class ProjectMembersWorkOnItsTasks
/// {
///     public static bool Allows(ProjectTask task, Caller caller) =&gt; ProjectMembers.Allows(task.ProjectId);
/// }
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// A module's contracts cannot hold the definition, which reads the module's own aggregate; this is the
/// part other modules may see. The Supabase build refuses a rule that asks a function no module defines.
/// </para>
/// <para>
/// A function that takes more than the key declares its question here, and the generator implements it:
/// <c>static partial bool Allows(ProjectId key, string role);</c> for one that answers about one
/// aggregate, or <c>static partial AccessSet&lt;ProjectId&gt; Ids(string role);</c> for one that answers
/// with the keys of every aggregate it allows. A contract that declares nothing gets
/// <c>Allows(<typeparamref name="TKey"/>)</c>, or <c>Ids()</c> with <see cref="Shape"/> set to
/// <see cref="AccessFunctionShape.Set"/>.
/// </para>
/// <para>
/// A name without a schema is relative to the module whose contracts declare it: <c>is_member</c> in the
/// Projects module is <c>projects/is_member</c>, which is also what the generated <c>Name</c> says, so a
/// definition named <c>[AccessFunction&lt;Project&gt;(ProjectMembers.Name)]</c> matches it wherever it lives.
/// </para>
/// </remarks>
/// <typeparam name="TKey">The key of the aggregate the function is about: the one value its rules pass it.</typeparam>
/// <param name="name">The function's name: <c>projects.is_member</c>, or <c>is_member</c>, relative to the module.</param>
#pragma warning disable S2326 // Unused type parameters should be removed (read by the source generator).
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AccessFunctionContractAttribute<TKey>(string name) : Attribute
#pragma warning restore S2326
{
    /// <summary>The function's name, as written: <c>projects.is_member</c>, <c>is_member</c> or <c>projects/is_member</c>.</summary>
    public string Name { get; } = name;

    /// <summary>
    /// Whether the function answers about one aggregate, given its key (<see cref="AccessFunctionShape.Row"/>,
    /// the default), or with the keys of every aggregate it allows (<see cref="AccessFunctionShape.Set"/>).
    /// A contract that declares its question takes the shape from it.
    /// </summary>
    public AccessFunctionShape Shape { get; set; } = AccessFunctionShape.Row;
}
