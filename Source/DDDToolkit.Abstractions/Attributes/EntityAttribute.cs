#nullable enable
using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public sealed class EntityAttribute<TType> : Attribute where TType : IEntityId
{

}
