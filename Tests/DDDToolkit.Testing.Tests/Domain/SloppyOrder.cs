using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Interfaces;

namespace DDDToolkit.Testing.Tests.Domain;

/// <summary>
/// An aggregate that is deliberately wrong: it raises an event and then refuses the command. That is
/// the bug <c>WhenThrows</c> exists to catch, so the tests need a domain object that has it.
/// </summary>
[AggregateRoot<Guid>("SLP")]
public partial class SloppyOrder
{
    /// <summary>Creates the order. Nothing is raised here, so each test starts from an empty list.</summary>
    public SloppyOrder(SloppyOrderId id) : base(id)
    {
    }

    /// <summary>Raises <see cref="OrderCancelled"/> and only then discovers the order cannot be cancelled.</summary>
    public void Cancel(string reason)
    {
        RaiseDomainEvent(new OrderCancelled(OrderId.Empty, reason));
        throw new InvalidOperationException("A shipped order cannot be cancelled.");
    }

    /// <summary>Test seam: raises whatever it is given, so a test can build any batch it needs.</summary>
    public void Raise(IDomainEvent domainEvent) => RaiseDomainEvent(domainEvent);

    /// <summary>Test seam: drains its own events, which is something an aggregate must never really do.</summary>
    public void DrainItself() => ((IHasDomainEvents)this).ClearDomainEvents();
}
