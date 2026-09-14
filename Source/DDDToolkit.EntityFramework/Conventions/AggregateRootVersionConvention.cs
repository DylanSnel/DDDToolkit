using DDDToolkit.Abstractions.Interfaces;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace DDDToolkit.EntityFramework.Conventions;

/// <summary>
/// Maps <see cref="IAggregateRoot.Version"/> of every aggregate root as an optimistic concurrency
/// token. The setter is private, which is fine for EF Core; explicit configuration in
/// <c>OnModelCreating</c> still wins over this convention.
/// </summary>
public sealed class AggregateRootVersionConvention : IModelFinalizingConvention
{
    /// <inheritdoc />
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            if (!typeof(IAggregateRoot).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var property = entityType.FindProperty(nameof(IAggregateRoot.Version))
                ?? entityType.Builder.Property(typeof(long), nameof(IAggregateRoot.Version))?.Metadata;

            property?.Builder.IsConcurrencyToken(true);
        }
    }
}
