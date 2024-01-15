namespace DDDToolkit.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Class)]
#pragma warning disable S2326 // Unused type parameters should be removed
public class SingleValueObjectAttribute<TType> : SingleValueObjectAttribute
#pragma warning restore S2326 // Unused type parameters should be removed
{

}

[AttributeUsage(AttributeTargets.Class)]
public abstract class SingleValueObjectAttribute : Attribute
{
    private protected SingleValueObjectAttribute() { }
}
