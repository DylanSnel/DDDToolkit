using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Tenancy;

public abstract partial class InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>
{
    /// <summary>An open invitation is for one valid address; one that is over has forgotten it.</summary>
    public sealed class AddressIsValid : IInvariant<InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>>
    {
        /// <inheritdoc />
        public string Code => TenancyRefusals.AddressInvalid;

        /// <inheritdoc />
        public InvariantFailure? Check(InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId> entity)
            => (entity.State == InvitationState.Open ? entity.Address is { } address && ValidAddress(address) == address : entity.Address is null)
                ? null
                : TenancyRefusals.Failure(TenancyRefusals.AddressInvalid, ("Max", MaxAddressLength));
    }
}
