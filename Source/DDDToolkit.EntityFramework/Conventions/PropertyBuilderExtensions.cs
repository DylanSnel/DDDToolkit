using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDDToolkit.EntityFramework.Conventions;

/// <summary>Says, where a property is configured, what the toolkit holds its column to.</summary>
public static class PropertyBuilderExtensions
{
    /// <summary>
    /// The value is written when the row is added and never changed afterwards: for a tenant, an owner fixed at
    /// creation, a number that is the row's identity to people.
    /// <code>
    /// project.Property(row => row.Number).IsFixedAfterInsert();
    /// </code>
    /// <para>
    /// Entity Framework then throws on a save that changed it, as it does for a key, and the privileges
    /// <c>DDDToolkit.EntityFramework.Postgres</c> writes from the policies (<c>RowAccessExport.WriteGrants</c>)
    /// leave the column out of <c>UPDATE</c>, so a statement that goes around the model is refused by the
    /// database as well. It sets the property's after-save behavior to <see cref="PropertySaveBehavior.Throw"/>,
    /// which is Entity Framework's own notion: this is configuration of the mapping, and needs no migration.
    /// </para>
    /// </summary>
    /// <param name="property">The property whose value is fixed once its row exists.</param>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is null.</exception>
    public static PropertyBuilder IsFixedAfterInsert(this PropertyBuilder property)
    {
        ArgumentNullException.ThrowIfNull(property);

        property.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        return property;
    }

    /// <inheritdoc cref="IsFixedAfterInsert(PropertyBuilder)"/>
    public static PropertyBuilder<TProperty> IsFixedAfterInsert<TProperty>(this PropertyBuilder<TProperty> property)
    {
        IsFixedAfterInsert((PropertyBuilder)property);
        return property;
    }
}
