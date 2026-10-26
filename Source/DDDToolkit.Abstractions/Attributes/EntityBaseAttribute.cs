namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Marks an abstract parent that a package ships for child entities declared elsewhere to derive from.
/// Apply to an <c>abstract partial class</c> whose first type parameter is the id. It is the entity
/// counterpart of <see cref="AggregateRootBaseAttribute"/>, and a concrete class chooses it through an
/// attribute the package marks with <see cref="EntityTemplateAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EntityBaseAttribute : Attribute
{
}
