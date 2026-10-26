using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Tenancy;

public abstract partial class OrganizationAggregate<TTenantId, TUnit, TUnitId>
{
    /// <summary>No unit is deeper than <see cref="MaxDepth"/>.</summary>
    public sealed class DepthAtMostMax : IInvariant<OrganizationAggregate<TTenantId, TUnit, TUnitId>>
    {
        /// <inheritdoc />
        public string Code => TenancyRefusals.DepthExceeded;

        /// <inheritdoc />
        public InvariantFailure? Check(OrganizationAggregate<TTenantId, TUnit, TUnitId> entity)
        {
            var byId = entity.ById();
            return entity._units.All(unit => AncestorsOf(unit, byId).Count <= MaxDepth)
                ? null
                : TenancyRefusals.Failure(TenancyRefusals.DepthExceeded, ("Max", MaxDepth));
        }
    }
}
