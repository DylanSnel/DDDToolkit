using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Tenancy;

public abstract partial class OrganizationAggregate<TTenantId, TUnit, TUnitId>
{
    /// <summary>The organization has a name of 1 to <see cref="MaxNameLength"/> characters.</summary>
    public sealed class NameIsValid : IInvariant<OrganizationAggregate<TTenantId, TUnit, TUnitId>>
    {
        /// <inheritdoc />
        public string Code => TenancyRefusals.NameInvalid;

        /// <inheritdoc />
        public InvariantFailure? Check(OrganizationAggregate<TTenantId, TUnit, TUnitId> entity)
            => TenancyNames.IsValid(entity.Name, MaxNameLength)
                ? null
                : TenancyNames.RequiredFailure(TenancyNames.TenantNameToken, MaxNameLength);
    }
}
