namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Marks a value object: an immutable type compared by its contents rather than by identity.
/// Apply to a <c>partial record</c> that is not sealed.
/// </summary>
/// <remarks>
/// Structs are accepted by the attribute so that applying it to one reports DDD00001, which explains
/// the requirement, instead of the compiler's generic "attribute is not valid on this declaration type".
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public class ValueObjectAttribute : Attribute
{
}
