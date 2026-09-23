namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Marks a value object: an immutable type compared by its contents rather than by identity.
/// Apply to a <c>partial record</c> that is not sealed: a positional one such as
/// <c>record Money(decimal Amount, string Currency)</c>, or one whose properties you declare as
/// <c>{ get; protected init; }</c>.
/// </summary>
/// <remarks>
/// <para>
/// The properties are <c>protected init</c> so no caller can use <c>with</c> to copy the value into a
/// state that never passed validation. For a positional record the generator declares them that way
/// itself, since the compiler would make them <c>public init</c>. Callers change a value with the
/// generated <c>With(...)</c> instead, which on the always-valid twin throws when the copy is invalid.
/// </para>
/// <para>
/// Structs are accepted by the attribute so that applying it to one reports DDD00001, which explains
/// the requirement, instead of the compiler's generic "attribute is not valid on this declaration type".
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public class ValueObjectAttribute : Attribute
{
}
