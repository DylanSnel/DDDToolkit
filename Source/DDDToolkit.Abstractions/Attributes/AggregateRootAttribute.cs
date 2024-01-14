#nullable enable
using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class AggregateRoot<TType> : Attribute where TType : IEntityId

{

}
