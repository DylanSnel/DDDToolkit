namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Pins the name of a domain event, so it keeps the name it was stored under when the class is renamed.
/// Used wherever an event is serialized by name, for example the EF Core outbox.
/// <para>
/// Without it an event is named by convention: its module and its class name in kebab case, so
/// <c>OrderPlaced</c> in <c>[assembly: Module("Ordering")]</c> is <c>ordering.order-placed</c>. That name
/// follows the class. Rename <c>OrderPlaced</c> and rows already written as <c>ordering.order-placed</c> no
/// longer find it, which is the moment to put the old name here:
/// </para>
/// <code>
/// [DomainEventName("ordering.order-placed")]
/// public sealed record PlacedOrder(OrderId OrderId) : DomainEvent;
/// </code>
/// <para>
/// Two events of one module with the same name and version are a compile error (DDD00036), and pinning one
/// of them to another name is the fix.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class DomainEventNameAttribute : Attribute
{
    public DomainEventNameAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A domain event name cannot be empty.", nameof(name));
        }

        Name = name;
    }

    public string Name { get; }
}
