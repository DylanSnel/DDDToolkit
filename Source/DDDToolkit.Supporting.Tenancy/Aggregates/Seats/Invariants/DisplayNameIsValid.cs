using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Tenancy;

public abstract partial class SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
{
    /// <summary>A seat has a display name of 1 to <see cref="MaxDisplayNameLength"/> characters.</summary>
    public sealed class DisplayNameIsValid : IInvariant<SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>>
    {
        /// <inheritdoc />
        public string Code => TenancyRefusals.NameInvalid;

        /// <inheritdoc />
        public InvariantFailure? Check(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId> entity)
            => TenancyNames.IsValid(entity.DisplayName, MaxDisplayNameLength)
                ? null
                : TenancyNames.RequiredFailure(TenancyNames.DisplayNameToken, MaxDisplayNameLength);
    }
}
