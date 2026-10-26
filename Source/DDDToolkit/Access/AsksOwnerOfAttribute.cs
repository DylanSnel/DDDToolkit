namespace DDDToolkit.Access;

/// <summary>
/// Says, of an assembly, the id of an aggregate whose owner its requests ask about
/// (<see cref="AccessRequirement.OwnerOf{TId}"/>): written by the toolkit's generator, once per id, for every line it
/// writes of <c>[Own]</c> or <c>[OrOwn]</c> on the id of an aggregate that marks its owner, and for every call of
/// <c>AccessRequirement.OwnerOf</c> the project writes itself.
/// <code>
/// [assembly: AsksOwnerOf(typeof(BoardId))]
/// </code>
/// </summary>
/// <remarks>
/// Read at start-up, so the owner of each such aggregate is found where it is kept before the first request asks for
/// it: the Entity Framework package's check <c>entity-framework.owners-mapped</c> reads it from the assembly of every
/// request interface the host registered checks for, and finds the context that maps each aggregate, its
/// <c>[Owner]</c> and the column, as the first request would. An application writes none of it.
/// </remarks>
/// <param name="aggregateId">The id of the aggregate whose owner is asked about.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class AsksOwnerOfAttribute(Type aggregateId) : Attribute
{
    /// <summary>The id of the aggregate whose owner is asked about.</summary>
    public Type AggregateId { get; } = aggregateId;
}
