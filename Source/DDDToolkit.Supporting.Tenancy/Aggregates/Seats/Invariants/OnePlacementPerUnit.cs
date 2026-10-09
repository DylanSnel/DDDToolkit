using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Tenancy;

public abstract partial class SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
{
    /// <summary>A seat is placed in a unit at most once.</summary>
    public sealed class OnePlacementPerUnit : IInvariant<SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>>
    {
        /// <inheritdoc />
        public string Code => TenancyRefusals.DuplicatePlacement;

        /// <inheritdoc />
        public InvariantFailure? Check(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId> entity)
            => entity._placements.Select(placement => placement.UnitId).Distinct().Count() == entity._placements.Count
                ? null
                : TenancyRefusals.Failure(TenancyRefusals.DuplicatePlacement);
    }
}
