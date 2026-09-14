using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace DDDToolkit.EntityFramework.Conventions;

#pragma warning disable EF1001 // CoreAnnotationNames is EF Core internal API; only its constant names are used.

/// <summary>
/// Maps the read-only collection properties the DDDToolkit generator emits for
/// <c>partial IReadOnlyList&lt;T&gt; Items { get; }</c> as EF Core primitive collections when
/// <typeparamref name="T"/> is a primitive or value-converted type (struct ids, single value objects,
/// strings, numbers).
/// <para>
/// EF Core only discovers primitive properties that have a setter; get-only properties are silently
/// skipped, so without this convention such a collection would never reach the database. Collections
/// of entity types are left alone: relationship discovery already maps them as navigations. When the
/// element type has a pre-convention conversion (<c>Properties&lt;T&gt;().HaveConversion(...)</c>, which the
/// generated <c>Add{Module}Converters</c> registers) the same converter, max length and unicode setting
/// are applied to the element type, which EF Core does not do on its own.
/// </para>
/// <para>
/// Only arrays and <see cref="IList{T}"/> implementations can be primitive collections in EF Core. A
/// generated <c>IReadOnlySet&lt;T&gt;</c> (backed by <see cref="HashSet{T}"/>) of primitives therefore
/// cannot be mapped; this convention throws for it instead of leaving it unmapped, because a property
/// the generator created for persistence should not vanish silently. Use <c>IReadOnlyList&lt;T&gt;</c>,
/// or ignore the property explicitly.
/// </para>
/// </summary>
public sealed class ReadOnlyCollectionConvention : IEntityTypeAddedConvention
{
    /// <inheritdoc />
    public void ProcessEntityTypeAdded(IConventionEntityTypeBuilder entityTypeBuilder, IConventionContext<IConventionEntityTypeBuilder> context)
    {
        var entityType = entityTypeBuilder.Metadata;
        var clrType = entityType.ClrType;

        foreach (var property in clrType.GetRuntimeProperties())
        {
            if (!IsGetOnlyInstanceProperty(property) || entityType.IsIgnored(property.Name))
            {
                continue;
            }

            if (entityType.FindProperty(property.Name) is not null
                || entityType.FindNavigation(property.Name) is not null
                || entityType.FindSkipNavigation(property.Name) is not null
                || entityType.FindComplexProperty(property.Name) is not null)
            {
                continue;
            }

            var elementType = GetSequenceElementType(property.PropertyType);
            if (elementType is null)
            {
                continue;
            }

            var model = entityType.Model;
            var configuration = FindTypeMappingConfiguration(model, elementType);
            if (!IsPrimitiveCandidate(elementType, configuration))
            {
                continue;
            }

            var backingField = FindBackingField(clrType, property);
            var collectionType = backingField?.FieldType ?? property.PropertyType;
            if (!IsSupportedPrimitiveCollectionType(collectionType, elementType))
            {
                if (backingField is null)
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"'{clrType.Name}.{property.Name}' is a generated read-only collection backed by '{collectionType.Name}', but EF Core can only map primitive collections that are arrays or implement IList<T>. " +
                    $"Declare it as IReadOnlyList<{elementType.Name}> instead, or exclude it with [NotMapped] / Ignore().");
            }

            var propertyBuilder = entityTypeBuilder.Property(property);
            if (propertyBuilder is null)
            {
                continue;
            }

            propertyBuilder.SetElementType(elementType);
            ApplyElementConfiguration(propertyBuilder.Metadata.GetElementType(), configuration);
        }
    }

    private static bool IsGetOnlyInstanceProperty(PropertyInfo property)
        => property.CanRead
            && property.GetMethod is { IsPublic: true, IsStatic: false }
            && property.SetMethod is null
            && property.GetIndexParameters().Length == 0;

    /// <summary>The T of an IEnumerable&lt;T&gt; property type, excluding string and byte[].</summary>
    private static Type? GetSequenceElementType(Type type)
    {
        if (type == typeof(string) || type == typeof(byte[]))
        {
            return null;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return type.GetGenericArguments()[0];
        }

        return type.GetInterfaces()
            .Where(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .Select(static i => i.GetGenericArguments()[0])
            .FirstOrDefault();
    }

    private static bool IsPrimitiveCandidate(Type elementType, ITypeMappingConfiguration? configuration)
    {
        if (configuration is not null)
        {
            return true;
        }

        if (elementType == typeof(string) || elementType == typeof(byte[]))
        {
            return true;
        }

        // Structs (struct ids, numbers, Guid, dates, their nullable forms) but not interfaces or classes,
        // which are entity candidates for relationship discovery.
        return elementType.IsValueType;
    }

    private static ITypeMappingConfiguration? FindTypeMappingConfiguration(IConventionModel model, Type elementType)
    {
        // Pre-convention configuration is exposed on IModel only; the concrete Model implements both.
        if (model is not IModel fullModel)
        {
            return null;
        }

        var underlying = Nullable.GetUnderlyingType(elementType) ?? elementType;
        return fullModel.FindTypeMappingConfiguration(elementType) ?? fullModel.FindTypeMappingConfiguration(underlying);
    }

    private static void ApplyElementConfiguration(IConventionElementType? elementType, ITypeMappingConfiguration? configuration)
    {
        if (elementType is null || configuration is null)
        {
            return;
        }

        var builder = elementType.Builder;

        if (configuration.GetValueConverter() is { } converter)
        {
            builder.HasConversion(converter);
        }
        else if (configuration[CoreAnnotationNames.ValueConverterType] is Type converterType)
        {
            // HaveConversion<TConverter>() records the converter type only; EF instantiates it per property.
            builder.HasConverter(converterType);
        }
        else if (configuration.GetProviderClrType() is { } providerClrType)
        {
            builder.HasConversion(providerClrType);
        }

        if (configuration.GetMaxLength() is { } maxLength)
        {
            builder.HasMaxLength(maxLength);
        }

        if (configuration.IsUnicode() is { } unicode)
        {
            builder.IsUnicode(unicode);
        }

        if (configuration.GetPrecision() is { } precision)
        {
            builder.HasPrecision(precision);
        }

        if (configuration.GetScale() is { } scale)
        {
            builder.HasScale(scale);
        }
    }

    private static FieldInfo? FindBackingField(Type clrType, PropertyInfo property)
    {
        var attribute = property.GetCustomAttribute<BackingFieldAttribute>();
        if (attribute is null)
        {
            return null;
        }

        for (var type = clrType; type is not null; type = type.BaseType)
        {
            var field = type.GetField(attribute.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field is not null)
            {
                return field;
            }
        }

        return null;
    }

    private static bool IsSupportedPrimitiveCollectionType(Type collectionType, Type elementType)
        => collectionType.IsArray
            || typeof(IList<>).MakeGenericType(elementType).IsAssignableFrom(collectionType);
}
