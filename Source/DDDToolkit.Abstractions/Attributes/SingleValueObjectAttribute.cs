namespace DDDToolkit.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Class)]
#pragma warning disable S2326 // Unused type parameters should be removed
#pragma warning disable CS9113 // Parameter is unread.
#pragma warning disable IDE1006 // Naming Styles
public class SingleValueObjectAttribute<TType>(int ColumnLength = -1) : SingleValueObjectAttribute
#pragma warning restore IDE1006 // Naming Styles
#pragma warning restore CS9113 // Parameter is unread.
#pragma warning restore S2326 // Unused type parameters should be removed
{

}

[AttributeUsage(AttributeTargets.Class)]
public abstract class SingleValueObjectAttribute : Attribute
{
    private protected SingleValueObjectAttribute() { }
}
