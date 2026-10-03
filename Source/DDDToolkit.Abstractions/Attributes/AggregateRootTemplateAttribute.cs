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
/// <see cref="TemplateArgumentAttribute"/> on the same attribute class. Type arguments the attribute has
/// beyond what the parent takes are not the parent's: they say something more about the class, and only a
/// <see cref="TemplateRegistrationAttribute"/> method reads them, by position
/// (<see cref="TemplateTypeAttribute.Argument"/>).
/// </summary>
/// <param name="parent">The open generic parent, a class marked <see cref="AggregateRootBaseAttribute"/>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AggregateRootTemplateAttribute(Type parent) : Attribute
{
    /// <summary>The open generic parent a class declared with the attribute derives from.</summary>
    public Type Parent { get; } = parent;

    /// <summary>
    /// Whether an application may declare more than one class with the template in one project, or in the
    /// projects of one module; false by default. It means for an aggregate root what
    /// <see cref="EntityTemplateAttribute.AllowSeveral"/> means for an entity: each class gets its own
    /// registration, named after it.
    /// </summary>
    public bool AllowSeveral { get; set; }
}
