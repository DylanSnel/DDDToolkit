using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Interfaces;

public interface IHasDomainEvents
{
    [Internal]
    public IReadOnlyList<IDomainEvent> DomainEvents { get; }

    public void ClearDomainEvents();
}
