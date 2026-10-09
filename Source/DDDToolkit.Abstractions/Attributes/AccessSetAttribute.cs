namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Marks a <c>static partial</c> method of an <see cref="AccessFunctionsAttribute"/> class as a set-shaped
/// question an SQL function answers, <c>RETURNS SETOF</c>. The method returns
/// <see cref="Access.AccessSet{T}"/>, and a rule asks whether a value is in it:
/// <code>
/// [AccessSet("ids_i_follow")]
/// public static partial AccessSet&lt;TId&gt; IdsIFollow&lt;TId&gt;(string kind) where TId : IEntityId;
///
/// // in a rule
/// =&gt; Questions.IdsIFollow&lt;ProjectId&gt;("projects").Contains(project.Id);
/// </code>
/// The policy asks it once per statement, <c>"Id" = ANY (ARRAY(SELECT projects.ids_i_follow('projects')))</c>,
/// so its arguments are constants, the caller or the function's own parameters, never the row: an argument
/// that reads the row is DDD00051.
/// </summary>
/// <param name="name">
/// The function's name: <c>schema.name</c>, a function of your own; <c>owner/name</c>; or <c>name</c>,
/// relative to the class's owner. See <see cref="AccessFunctionsAttribute"/>.
/// </param>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class AccessSetAttribute(string name) : Attribute
{
    /// <summary>The function's name, as written: <c>schema.name</c>, <c>owner/name</c> or <c>name</c>.</summary>
    public string Name { get; } = name;
}
