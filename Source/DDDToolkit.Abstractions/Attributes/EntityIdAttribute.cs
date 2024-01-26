namespace DDDToolkit.Abstractions.Attributes;

#pragma warning disable CS9113 // Parameter is unread.
#pragma warning disable IDE1006 // Naming Styles
[AttributeUsage(AttributeTargets.Class)]
#pragma warning disable S2326 // Unused type parameters should be removed
public sealed class EntityIdAttribute<TType>(string Prefix = "", int ColumnLength = -1) : EntityIdAttribute
#pragma warning restore S2326 // Unused type parameters should be removed
#pragma warning restore IDE1006 // Naming Styles
#pragma warning restore CS9113 // Parameter is unread.
{

}

[AttributeUsage(AttributeTargets.Class)]
public abstract class EntityIdAttribute : Attribute
{
    internal EntityIdAttribute() { }
}
