using System.Reflection;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// Keeps every class whose tenant is marked <see cref="OfTenantAttribute"/> to the current Tenancy caller's tenant, as
/// <see cref="TenancyModelBuilderExtensions.ScopeToTenant"/> keeps a class it is called on: the tenant filter on its
/// reads, and Tenancy's save check on its writes. A context needs no line for it:
/// <code>
/// [AggregateRoot&lt;ProjectId&gt;]
/// public sealed partial class Project
/// {
///     [OfTenant] public TenantId TenantId { get; private set; }
/// }
///
/// // In the context: nothing for the tenant.
/// protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
///     => configurationBuilder.AddDDDToolkitConventions();
/// </code>
/// <para>
/// The marker names this convention, and <c>AddDDDToolkitConventions()</c> runs it in every model that maps a class
/// with the marker, when the model is finalized. It gives the class what <c>ScopeToTenant(row =&gt; row.TenantId)</c>
/// gives: the named filter <see cref="TenancyQueryFilter.Name"/> next to the class's other named filters, the
/// property for the save check, which <c>UseDDDToolkit</c> adds, and the property fixed once the row is there. A
/// class the context keeps to a tenant itself, with <c>ScopeToTenant</c>, keeps that mapping: the context's call wins.
/// </para>
/// <para>
/// The marked property is of the application's tenant id, never nullable, and a class has one. It sits on the root of
/// a hierarchy the context stores, since a filter is the root's. A type the context owns is kept through its owner,
/// so a marker on it maps nothing. A model that breaks one of these is refused while it is built, naming the class.
/// </para>
/// </summary>
public sealed class OfTenantConvention : IModelFinalizingConvention
{
    /// <inheritdoc />
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().ToList())
        {
            if (entityType.IsOwned() || entityType.HasSharedClrType || MarkedOn(entityType.ClrType) is not { } tenant)
            {
                continue;
            }

            if (entityType.BaseType is { } parent)
            {
                // A derived type is kept with its root, which declares the property as well.
                if (!tenant.DeclaringType!.IsAssignableFrom(entityType.GetRootType().ClrType))
                {
                    throw new InvalidOperationException(
                        $"'{entityType.DisplayName()}.{tenant.Name}' is marked [OfTenant], and '{entityType.DisplayName()}' derives from '{parent.DisplayName()}', "
                        + "which the context stores with it. A tenant filter is the root's: mark the tenant of the root of the hierarchy.");
                }

                continue;
            }

            // Kept by hand, with ScopeToTenant: the context's call wins.
            if (entityType.FindAnnotation(TenancyMapping.TenantPropertyAnnotation) is not null)
            {
                continue;
            }

            Scope(modelBuilder.Metadata, entityType, tenant);
        }
    }

    /// <summary>
    /// What <c>ScopeToTenant</c> gives an entity type, through the builders a convention has: the tenant filter, the
    /// property the save check reads, the requirement of the save check, and the property fixed after the insert.
    /// </summary>
    private static void Scope(IConventionModel model, IConventionEntityType entityType, PropertyInfo tenant)
    {
        if (!IsTenantId(tenant.PropertyType))
        {
            throw new InvalidOperationException(
                $"'{entityType.DisplayName()}.{tenant.Name}' is marked [OfTenant], and its type is {Written(tenant.PropertyType)}. The tenant of a row is the "
                + "application's tenant id, never nullable: a struct id, as TenantId.");
        }

        var property = entityType.Builder.Property(tenant, fromDataAnnotation: true)
            ?? throw new InvalidOperationException(
                $"'{entityType.DisplayName()}.{tenant.Name}' is marked [OfTenant], and cannot be mapped: the context ignores it. Map the property, or keep the "
                + "class to its tenant by hand with ScopeToTenant.");

        entityType.Builder.HasQueryFilter(TenancyQueryFilter.Name, TenancyQueryFilter.Of(entityType.ClrType, tenant), fromDataAnnotation: true);
        entityType.Builder.HasAnnotation(TenancyMapping.TenantPropertyAnnotation, tenant.Name, fromDataAnnotation: true);
        model.RequireContextPart(
            TenancySaveInterceptor.PartName,
            typeof(TenancySaveInterceptor),
            TenancyModelBuilderExtensions.TenancyRegistration,
            TenancyModelBuilderExtensions.TenancyCall);

        // A row never moves to another tenant, as with ScopeToTenant.
        property.AfterSave(PropertySaveBehavior.Throw, fromDataAnnotation: true);
    }

    /// <summary>The property of <paramref name="type"/> marked <see cref="OfTenantAttribute"/>, or <see langword="null"/>; throws for two.</summary>
    private static PropertyInfo? MarkedOn(Type type)
    {
        var marked = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(static property => property.IsDefined(typeof(OfTenantAttribute), inherit: false))
            .ToList();

        return marked.Count switch
        {
            0 => null,
            1 => marked[0],
            _ => throw new InvalidOperationException(
                $"'{type.Name}' marks {string.Join(" and ", marked.Select(static property => property.Name))} with [OfTenant]. A row belongs to one tenant: mark one property."),
        };
    }

    /// <summary>A type as it is written in C#, for a message: <c>TenantId?</c> rather than <c>Nullable`1</c>.</summary>
    private static string Written(Type type)
        => Nullable.GetUnderlyingType(type) is { } underlying ? underlying.Name + "?" : type.Name;

    /// <summary>Whether <paramref name="type"/> is a tenant id as <c>ScopeToTenant</c> takes one: a struct id, equatable to itself.</summary>
    private static bool IsTenantId(Type type)
        => type.IsValueType
           && Nullable.GetUnderlyingType(type) is null
           && typeof(IEntityId).IsAssignableFrom(type)
           && typeof(IEquatable<>).MakeGenericType(type).IsAssignableFrom(type);
}
