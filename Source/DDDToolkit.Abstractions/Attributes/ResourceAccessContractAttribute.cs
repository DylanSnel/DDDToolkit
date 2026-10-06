namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Publishes one set the access to a resource answers, the resources the caller sees or those it holds a key on,
/// for row access rules to ask by the resource's id, typed, with no function's name anywhere. Apply, in the
/// contracts of the module that owns the resource, to an empty <c>static partial class</c>; the generator adds
/// <c>Name</c> and <c>Ids(...)</c>:
/// <code>
/// // Projects.Contracts
/// [ModuleContract]
/// [ResourceAccessContract&lt;ProjectId&gt;(ResourceAccessSet.Seen)]
/// public static partial class ProjectsISee;
///
/// [ModuleContract]
/// [ResourceAccessContract&lt;ProjectId&gt;(ResourceAccessSet.HeldOn)]
/// public static partial class ProjectsWhereIHold;
///
/// // A rule of Inspections, and one of Projects' own, alike
/// public static bool Allows(Inspection inspection, Caller caller) =&gt; ProjectsISee.Ids().Contains(inspection.ProjectId);
/// public static bool Allows(Project project, Caller caller) =&gt; ProjectsWhereIHold.Ids(ProjectKeys.Edit).Contains(project.Id);
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// Which SQL function answers is not the contract's to say. The function is written by the package that keeps the
/// resource's access, under the name that package gives it: the Membership package writes the functions of a
/// resource with members under the names of its rules, which are taken from the resource's name unless the rules
/// keep names a database already has. The export finds the function that answers the set for the resource whose id
/// is <typeparamref name="TKey"/>, and writes the policy with its name. So the name is said in one place or in
/// none, and a rule that asks a set no package answers is refused when its access file is written, naming the rule.
/// </para>
/// <para>
/// A rule of another module asks the contract, and so references neither the owning module's aggregate nor the
/// package that answers: what the owning module publishes is that its projects are seen and held, and how is its
/// own. Its own rules ask the same contract, so a question has one name in every module. And only the owning
/// module declares one: a contract of an id whose assembly declares another module is DDD00038, so what a module
/// publishes of its resources stays its own to say, and another module asks what it publishes.
/// </para>
/// <para>
/// The class may declare its question itself, to write its own documentation on it, and the generator implements
/// it: <c>static partial AccessSet&lt;ProjectId&gt; Ids();</c> for <see cref="ResourceAccessSet.Seen"/>, and
/// <c>static partial AccessSet&lt;ProjectId&gt; Ids(string key);</c> for <see cref="ResourceAccessSet.HeldOn"/>.
/// Called in C#, <c>Ids</c> throws: only the database answers it.
/// </para>
/// </remarks>
/// <typeparam name="TKey">
/// The id of the resource's aggregate: what the set holds, and how the export finds the resource. One declared with
/// <c>[EntityId&lt;T&gt;]</c>, or, in the project that declares the aggregate, the one the toolkit writes for an
/// aggregate root declared with a value, <c>BoardId</c> for <c>[AggregateRoot&lt;Guid&gt;] public partial class Board</c>.
/// </typeparam>
/// <param name="set">Which set the contract publishes.</param>
#pragma warning disable S2326 // Unused type parameters should be removed (read by the source generator).
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ResourceAccessContractAttribute<TKey>(ResourceAccessSet set) : Attribute
#pragma warning restore S2326
{
    /// <summary>Which set the contract publishes.</summary>
    public ResourceAccessSet Set { get; } = set;
}
