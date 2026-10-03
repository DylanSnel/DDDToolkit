namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Names a type of this assembly that declares <see cref="TemplateRegistrationAttribute"/> methods, so the
/// generator in an application that references the assembly knows where to look:
/// <code>
/// [assembly: TemplateRegistrations(typeof(SubscriptionModelBuilderExtensions))]
/// </code>
/// <para>
/// The generator reads the attributes of every referenced assembly, and only the types they name. Without
/// it, finding the methods would mean walking every type of every reference on every build.
/// </para>
/// </summary>
/// <param name="declaringType">The type whose <see cref="TemplateRegistrationAttribute"/> methods an application gets closed over its classes.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class TemplateRegistrationsAttribute(Type declaringType) : Attribute
{
    /// <summary>The type that declares the registration methods.</summary>
    public Type DeclaringType { get; } = declaringType;
}
