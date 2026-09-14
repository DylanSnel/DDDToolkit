using System.Reflection;
using DDDToolkit.Abstractions.Attributes;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace DDDToolkit.EntityFramework.Conventions;

/// <summary>
/// Keeps members marked <see cref="InternalAttribute"/> out of the model, on entity types and complex
/// types alike. <c>[Internal]</c> means "auxiliary, never stored or serialized", so a property carrying
/// it is ignored the same way <c>[NotMapped]</c> would ignore it.
/// </summary>
public sealed class InternalMemberConvention : IEntityTypeAddedConvention, IComplexPropertyAddedConvention
{
    /// <inheritdoc />
    public void ProcessEntityTypeAdded(IConventionEntityTypeBuilder entityTypeBuilder, IConventionContext<IConventionEntityTypeBuilder> context)
        => IgnoreInternalMembers(entityTypeBuilder);

    /// <inheritdoc />
    public void ProcessComplexPropertyAdded(IConventionComplexPropertyBuilder propertyBuilder, IConventionContext<IConventionComplexPropertyBuilder> context)
        => IgnoreInternalMembers(propertyBuilder.Metadata.ComplexType.Builder);

    private static void IgnoreInternalMembers(IConventionTypeBaseBuilder typeBuilder)
    {
        foreach (var property in typeBuilder.Metadata.ClrType.GetRuntimeProperties())
        {
            if (property.GetCustomAttribute<InternalAttribute>(inherit: true) is null)
            {
                continue;
            }

            if (typeBuilder.CanIgnore(property.Name))
            {
                typeBuilder.Ignore(property.Name);
            }
        }
    }
}
