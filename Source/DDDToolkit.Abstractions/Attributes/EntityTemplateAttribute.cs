namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Turns an attribute a package declares into a way to declare a child entity that derives from one of
/// the package's parents. It is the entity counterpart of <see cref="AggregateRootTemplateAttribute"/>.
/// </summary>
/// <param name="parent">The open generic parent, a class marked <see cref="EntityBaseAttribute"/>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EntityTemplateAttribute(Type parent) : Attribute
{
    /// <summary>The open generic parent a class declared with the attribute derives from.</summary>
    public Type Parent { get; } = parent;
}
