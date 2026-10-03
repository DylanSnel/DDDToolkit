using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Tenancy;

public abstract partial class SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
{
    /// <summary>A placement holds a role at most once.</summary>
    public sealed class OneGrantPerRolePerPlacement : IInvariant<SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>>
    {
        /// <inheritdoc />
        public string Code => TenancyRefusals.DuplicateGrant;

        /// <inheritdoc />
        public InvariantFailure? Check(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId> entity)
            => entity._placements.All(placement => placement.Grants.Select(grant => grant.RoleId).Distinct().Count() == placement.Grants.Count)
                ? null
                : TenancyRefusals.Failure(TenancyRefusals.DuplicateGrant);
    }
}
