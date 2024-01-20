using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.BaseTypes;

public abstract class AggregateRoot<TIdObject> : Entity<TIdObject>
    where TIdObject : IEntityId
{
    //public new TIdObject Id { get; }

    protected AggregateRoot(TIdObject id)
        => Id = id;

#pragma warning disable CS8618
    protected AggregateRoot()
    {
    }
#pragma warning restore CS8618
}
