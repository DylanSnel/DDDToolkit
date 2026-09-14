namespace DDDToolkit.Abstractions.Interfaces;

/// <summary>Marker for strongly typed identifiers.</summary>
public interface IEntityId
{
}

/// <summary>A strongly typed identifier wrapping a single <typeparamref name="TValue"/>.</summary>
public interface IEntityId<out TValue> : IEntityId
{
    TValue Value { get; }
}
