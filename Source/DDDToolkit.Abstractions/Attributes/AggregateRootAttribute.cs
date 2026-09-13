using System.ComponentModel;
using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Marks an aggregate root identified by a <typeparamref name="TType"/>: the consistency boundary of a
/// cluster of entities and value objects. Apply to a <c>partial class</c>.
/// </summary>
/// <remarks>
/// Records and structs are accepted by the attribute so that applying it to one reports DDD00002,
/// which explains the requirement, instead of the compiler's generic "attribute is not valid on this
/// declaration type".
/// </remarks>
#pragma warning disable S2326 // Unused type parameters should be removed (read by the source generator).
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public class AggregateRootAttribute<TType> : AggregateRootAttribute where TType : IEntityId
#pragma warning restore S2326
{
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class AggregateRootAttribute : Attribute
{
    internal AggregateRootAttribute() { }
}
