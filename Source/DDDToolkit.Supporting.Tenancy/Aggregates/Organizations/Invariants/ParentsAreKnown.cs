using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Tenancy;

public abstract partial class OrganizationAggregate<TTenantId, TUnit, TUnitId>
{
    /// <summary>Every unit's parent is a unit of the organization.</summary>
    public sealed class ParentsAreKnown : IInvariant<OrganizationAggregate<TTenantId, TUnit, TUnitId>>
    {
        /// <inheritdoc />
        public string Code => TenancyRefusals.UnitNotFound;

        /// <inheritdoc />
        public InvariantFailure? Check(OrganizationAggregate<TTenantId, TUnit, TUnitId> entity)
        {
            var byId = entity.ById();
            return entity._units.All(unit => unit.ParentId is not { } parent || byId.ContainsKey(parent))
                ? null
                : TenancyRefusals.Failure(TenancyRefusals.UnitNotFound);
        }
    }
}
