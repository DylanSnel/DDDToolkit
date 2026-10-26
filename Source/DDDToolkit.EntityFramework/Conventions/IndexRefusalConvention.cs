using DDDToolkit.EntityFramework.Interceptors;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace DDDToolkit.EntityFramework.Conventions;

/// <summary>
/// Checks every index that says what it refuses with (<c>RefusesAs</c>) when the model is built, so a mistake
/// shows at start-up rather than on the day two saves race: the index has to be unique, since no other index
/// refuses a save, and every property its message names in braces has to be one the entity type maps, since
/// the refusal fills it from the row.
/// </summary>
public sealed class IndexRefusalConvention : IModelFinalizingConvention
{
    /// <inheritdoc />
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (var index in entityType.GetDeclaredIndexes())
            {
                if (IndexRefusal.Of(index) is not { } refusal)
                {
                    continue;
                }

                var where = "The index on (" + string.Join(", ", index.Properties.Select(property => property.Name)) + ") of '" + entityType.DisplayName()
                    + "' is refused as '" + refusal.Code + "'";

                if (!index.IsUnique)
                {
                    throw new InvalidOperationException(where + ", but it is not unique, so no save ever breaks it. Call IsUnique() on it, or take RefusesAs away.");
                }

                foreach (var name in RefusalTemplate.Placeholders(refusal.Message))
                {
                    if (IndexRefusal.PropertyNamed(entityType, name) is null)
                    {
                        throw new InvalidOperationException(
                            where + ", and its message names {" + name + "}, which is not a property '" + entityType.DisplayName() + "' maps. "
                            + "A placeholder is filled from the row that broke the index: name one of its properties, or write {{ and }} for literal braces.");
                    }
                }
            }
        }
    }
}
