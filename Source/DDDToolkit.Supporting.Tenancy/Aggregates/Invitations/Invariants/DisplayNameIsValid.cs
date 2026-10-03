using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Tenancy;

public abstract partial class InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>
{
    /// <summary>A suggested name is at most <see cref="MaxDisplayNameLength"/> characters.</summary>
    public sealed class DisplayNameIsValid : IInvariant<InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>>
    {
        /// <inheritdoc />
        public string Code => TenancyRefusals.NameInvalid;

        /// <inheritdoc />
        public InvariantFailure? Check(InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId> entity)
            => entity.DisplayName is null || TenancyNames.IsValid(entity.DisplayName, MaxDisplayNameLength)
                ? null
                : TenancyNames.OptionalFailure(TenancyNames.DisplayNameToken, MaxDisplayNameLength);
    }
}
