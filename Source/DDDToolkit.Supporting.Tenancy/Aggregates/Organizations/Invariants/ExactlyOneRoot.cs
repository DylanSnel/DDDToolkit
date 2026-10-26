using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Tenancy;

public abstract partial class OrganizationAggregate<TTenantId, TUnit, TUnitId>
{
    /// <summary>An organization has exactly one root.</summary>
    public sealed class ExactlyOneRoot : IInvariant<OrganizationAggregate<TTenantId, TUnit, TUnitId>>
    {
        /// <inheritdoc />
        public string Code => TenancyRefusals.OneRoot;

        /// <inheritdoc />
        public InvariantFailure? Check(OrganizationAggregate<TTenantId, TUnit, TUnitId> entity)
            => entity._units.Count(unit => unit.ParentId is null) == 1 ? null : TenancyRefusals.Failure(TenancyRefusals.OneRoot);
    }
}
