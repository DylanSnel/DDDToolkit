namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Turns an attribute a package declares into a way to declare an aggregate root that derives from one of
/// the package's parents. Apply it to the attribute class, naming the open parent:
/// <code>
/// [AggregateRootTemplate(typeof(SubscriptionAggregate&lt;&gt;))]
/// [AttributeUsage(AttributeTargets.Class, Inherited = false)]
/// public sealed class SubscriptionAttribute&lt;TSubscriptionId&gt; : Attribute;
/// </code>
/// and an application then declares its own class with it, as it would with <c>[AggregateRoot&lt;TId&gt;]</c>:
/// <code>
/// [Subscription&lt;SubscriptionId&gt;]
/// public sealed partial class ShopSubscription
/// {
///     public bool IsTrial { get; private set; }
/// }
/// </code>
/// The generator writes <c>ShopSubscription : SubscriptionAggregate&lt;SubscriptionId&gt;</c> and everything an
/// aggregate root gets. The type arguments of the attribute fill the parent's first type parameters in
/// order, and the first of them is the id. A parent parameter the attribute does not fill is filled by a
/// <see cref="TemplateArgumentAttribute"/> on the same attribute class.
/// </summary>
/// <param name="parent">The open generic parent, a class marked <see cref="AggregateRootBaseAttribute"/>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AggregateRootTemplateAttribute(Type parent) : Attribute
{
    /// <summary>The open generic parent a class declared with the attribute derives from.</summary>
    public Type Parent { get; } = parent;
}
