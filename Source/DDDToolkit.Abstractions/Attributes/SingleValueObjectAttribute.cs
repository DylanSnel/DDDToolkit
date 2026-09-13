namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Marks a value object wrapping a single <typeparamref name="TType"/>, such as an email address or a
/// quantity. Apply to a <c>partial record</c> that is not sealed.
/// </summary>
/// <param name="ColumnLength">Optional maximum column length applied by the generated EF Core configuration.</param>
/// <remarks>
/// Structs are accepted by the attribute so that applying it to one reports DDD00001, which explains
/// the requirement, instead of the compiler's generic "attribute is not valid on this declaration type".
/// </remarks>
#pragma warning disable S2326 // Unused type parameters should be removed (read by the source generator).
#pragma warning disable CS9113 // Parameter is unread (read by the source generator).
#pragma warning disable IDE1006 // Naming Styles
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public class SingleValueObjectAttribute<TType>(int ColumnLength = -1) : SingleValueObjectAttribute
#pragma warning restore IDE1006
#pragma warning restore CS9113
#pragma warning restore S2326
{
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public abstract class SingleValueObjectAttribute : Attribute
{
    private protected SingleValueObjectAttribute() { }
}
