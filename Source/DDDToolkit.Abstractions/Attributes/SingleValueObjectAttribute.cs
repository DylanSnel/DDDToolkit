namespace DDDToolkit.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class SingleValueObjectAttribute<TType> : SingleValueObjectAttribute
{

}

[AttributeUsage(AttributeTargets.Class)]
public abstract class SingleValueObjectAttribute : Attribute
{
    internal SingleValueObjectAttribute() { }
}
