namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// This attribute is used to mark a property, field or method as internal.
/// This is used to indicate that the member is an auxiliary member and should not be stored/seriliazed/mapped.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method)]
public class InternalAttribute : Attribute
{
    public InternalAttribute() : base()
    {
    }
}
