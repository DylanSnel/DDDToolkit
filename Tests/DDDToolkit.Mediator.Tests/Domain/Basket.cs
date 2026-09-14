using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Mediator.Tests.Domain;

/// <summary>
/// The only aggregate of the test domain. Every method raises one event, so a test can decide
/// exactly which events a save carries and in which order.
/// </summary>
[AggregateRoot<BasketId>]
public partial class Basket
{
    public Basket(BasketId id, string name) : base(id)
    {
        Name = name;
        RaiseDomainEvent(new BasketCreated(id, name));
    }

    public string Name { get; private set; }

    /// <summary>Item names, backed by the generated <c>_items</c> list and stored as a primitive collection.</summary>
    public partial IReadOnlyList<string> Items { get; }

    public void AddItem(string item)
    {
        _items.Add(item);
        RaiseDomainEvent(new ItemAdded(Id, item));
    }

    public void Empty()
    {
        _items.Clear();
        RaiseDomainEvent(new BasketEmptied(Id));
    }

    /// <summary>Raises the event that does not implement <c>INotification</c>.</summary>
    public void Archive() => RaiseDomainEvent(new BasketArchived(Id));

    public void Rename(string name) => Name = name;
}
