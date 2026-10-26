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

    /// <summary>
    /// Whether the package makes the id of a new class of this template itself, in code and before the save, with
    /// <c>TId.Create()</c>, as Tenancy makes a new seat's. False by default, for an aggregate whose new instances the
    /// application makes. When it is set, the id a class is declared with has a <c>Create()</c>, which the generator
    /// writes for an id over a <see cref="Guid"/> and an id over anything else declares itself; a class declared over
    /// an id without one is DDD00067, on the class, rather than a compile error in code closed over it. The package's
    /// code that makes the ids asks the same of its type parameter: <c>where TId : ICreatableEntityId&lt;TId&gt;</c>.
    /// </summary>
    public bool CreatesIds { get; set; }
}
