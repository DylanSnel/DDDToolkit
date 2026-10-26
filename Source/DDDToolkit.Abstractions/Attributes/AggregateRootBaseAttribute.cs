namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Marks an abstract parent that a package ships for aggregate roots declared elsewhere to derive from.
/// Apply to an <c>abstract partial class</c> whose first type parameter is the id:
/// <code>
/// [AggregateRootBase]
/// public abstract partial class SubscriptionAggregate&lt;TSubscriptionId&gt;
///     where TSubscriptionId : IEntityId, IEquatable&lt;TSubscriptionId&gt;
/// {
///     public string Plan { get; private set; } = "";
/// }
/// </code>
/// <para>
/// The generator gives the parent what it gives every aggregate root: the base class, the backing fields
/// of its collections and the machinery that runs its nested <c>IInvariant&lt;T&gt;</c> rules. The class that
/// derives from it runs the parent's rules, seam and children before its own, so an application that
/// extends the package's aggregate does not lose the rules the package states.
/// </para>
/// <para>
/// A concrete class chooses the parent through an attribute the package marks with
/// <see cref="AggregateRootTemplateAttribute"/>, and the generator writes its base class.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AggregateRootBaseAttribute : Attribute
{
}
