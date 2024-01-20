#nullable enable
using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Class)]
#pragma warning disable S2326 // Unused type parameters should be removed
public class AggregateRootAttribute<TType> : AggregateRootAttribute where TType : IEntityId
#pragma warning restore S2326 // Unused type parameters should be removed

{

}

[AttributeUsage(AttributeTargets.Class)]
public abstract class AggregateRootAttribute : Attribute
{
    internal AggregateRootAttribute() { }
}

