using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Tenancy;

public abstract partial class OrganizationUnitEntity<TUnitId>
{
    /// <summary>A unit has a kind of 1 to <see cref="MaxKindLength"/> characters.</summary>
    public sealed class KindIsSet : IInvariant<OrganizationUnitEntity<TUnitId>>
    {
        /// <inheritdoc />
        public string Code => TenancyRefusals.KindInvalid;

        /// <inheritdoc />
        public InvariantFailure? Check(OrganizationUnitEntity<TUnitId> entity)
            => TenancyNames.IsValid(entity.Kind, MaxKindLength)
                ? null
                : TenancyRefusals.Failure(TenancyRefusals.KindInvalid, ("Kind", entity.Kind), ("Max", MaxKindLength));
    }
}
