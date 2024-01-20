#nullable enable
using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class EntityAttribute<TType> : EntityAttribute where TType : IEntityId
{

}

[AttributeUsage(AttributeTargets.Class)]
public abstract class EntityAttribute : Attribute
{
    internal EntityAttribute() { }
}
