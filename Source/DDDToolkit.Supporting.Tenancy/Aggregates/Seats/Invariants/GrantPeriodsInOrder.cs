using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Tenancy;

public abstract partial class SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
{
    /// <summary>Every grant with an end has it later than its start.</summary>
    public sealed class GrantPeriodsInOrder : IInvariant<SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>>
    {
        /// <inheritdoc />
        public string Code => TenancyRefusals.InvalidPeriod;

        /// <inheritdoc />
        public InvariantFailure? Check(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId> entity)
            => entity._placements.All(placement => placement.Grants.All(grant => grant.EndsAt is not { } end || end > grant.StartsAt))
                ? null
                : TenancyRefusals.Failure(TenancyRefusals.InvalidPeriod);
    }
}
