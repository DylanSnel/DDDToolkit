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
/// A module's contracts cannot hold the definition, which reads the module's own aggregate; this is the
/// part other modules may see. The Supabase build refuses a rule that asks a function no module defines.
/// </remarks>
/// <typeparam name="TKey">The key of the aggregate the function is about: the one value its rules pass it.</typeparam>
/// <param name="name">The function's name with its schema, <c>projects.is_member</c>.</param>
#pragma warning disable S2326 // Unused type parameters should be removed (read by the source generator).
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AccessFunctionContractAttribute<TKey>(string name) : Attribute
#pragma warning restore S2326
{
    /// <summary>The function's name with its schema, <c>projects.is_member</c>.</summary>
    public string Name { get; } = name;
}
