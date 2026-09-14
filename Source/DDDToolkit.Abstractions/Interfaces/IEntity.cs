namespace DDDToolkit.Abstractions.Interfaces;

/// <summary>Marker for entities: objects with identity.</summary>
public interface IEntity
{
}

/// <summary>An entity identified by a <typeparamref name="TId"/>.</summary>
public interface IEntity<out TId> : IEntity where TId : IEntityId
{
    TId Id { get; }
}
