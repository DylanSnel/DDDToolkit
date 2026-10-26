using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Tenancy;

public abstract partial class SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
{
    /// <summary>A seat has at most one primary placement.</summary>
    public sealed class AtMostOnePrimary : IInvariant<SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>>
    {
        /// <inheritdoc />
        public string Code => TenancyRefusals.SecondPrimary;

        /// <inheritdoc />
        public InvariantFailure? Check(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId> entity)
            => entity._placements.Count(placement => placement.IsPrimary) <= 1 ? null : TenancyRefusals.Failure(TenancyRefusals.SecondPrimary);
    }
}
