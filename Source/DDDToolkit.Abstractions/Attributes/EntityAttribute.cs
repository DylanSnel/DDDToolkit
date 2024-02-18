#nullable enable
using DDDToolkit.Abstractions.Interfaces;
using System.ComponentModel;

namespace DDDToolkit.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class EntityAttribute<TType> : EntityAttribute where TType : IEntityId
{

}

[AttributeUsage(AttributeTargets.Class)]
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class EntityAttribute : Attribute
{
    internal EntityAttribute() { }
}
