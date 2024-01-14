namespace DDDToolkit.BaseTypes;

public abstract class AggregateRoot<TIdObject, TIdType> : Entity<TIdObject>
    where TIdObject : EntityId<TIdType> where TIdType : notnull
{
    public new TIdObject Id { get; }

    protected AggregateRoot(TIdObject id)
        => Id = id;

#pragma warning disable CS8618
    protected AggregateRoot()
    {
    }
#pragma warning restore CS8618
}
