using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Tenancy;

public abstract partial class OrganizationAggregate<TTenantId, TUnit, TUnitId>
{
    /// <summary>No unit is above itself.</summary>
    public sealed class NoCycles : IInvariant<OrganizationAggregate<TTenantId, TUnit, TUnitId>>
    {
        /// <inheritdoc />
        public string Code => TenancyRefusals.Cycle;

        /// <inheritdoc />
        public InvariantFailure? Check(OrganizationAggregate<TTenantId, TUnit, TUnitId> entity)
        {
            var byId = entity.ById();
            foreach (var start in entity._units)
            {
                var visited = new HashSet<TUnitId> { start.Id };
                var unit = start;
                while (unit.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent))
                {
                    if (!visited.Add(parentId))
                    {
                        return TenancyRefusals.Failure(TenancyRefusals.Cycle);
                    }

                    unit = parent;
                }
            }

            return null;
        }
    }
}
