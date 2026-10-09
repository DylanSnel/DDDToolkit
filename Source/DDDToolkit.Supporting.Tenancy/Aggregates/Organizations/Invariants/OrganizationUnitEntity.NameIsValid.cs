using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Tenancy;

public abstract partial class OrganizationUnitEntity<TUnitId>
{
    /// <summary>A unit has a name of 1 to <see cref="MaxNameLength"/> characters.</summary>
    public sealed class NameIsValid : IInvariant<OrganizationUnitEntity<TUnitId>>
    {
        /// <inheritdoc />
        public string Code => TenancyRefusals.NameInvalid;

        /// <inheritdoc />
        public InvariantFailure? Check(OrganizationUnitEntity<TUnitId> entity)
            => TenancyNames.IsValid(entity.Name, MaxNameLength)
                ? null
                : TenancyNames.RequiredFailure(TenancyNames.UnitNameToken, MaxNameLength);
    }
}
