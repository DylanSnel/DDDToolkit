using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Tenancy;

public abstract partial class TenantAggregate<TTenantId>
{
    /// <summary>A suspended or closed tenant says why.</summary>
    public sealed class ClosedAndSuspendedHaveAReason : IInvariant<TenantAggregate<TTenantId>>
    {
        /// <inheritdoc />
        public string Code => TenancyRefusals.ReasonRequired;

        /// <inheritdoc />
        public InvariantFailure? Check(TenantAggregate<TTenantId> entity)
            => entity.Status is TenantStatus.Suspended or TenantStatus.Closed && string.IsNullOrWhiteSpace(entity.StatusReason)
                ? TenancyRefusals.Failure(TenancyRefusals.ReasonRequired)
                : null;
    }
}
