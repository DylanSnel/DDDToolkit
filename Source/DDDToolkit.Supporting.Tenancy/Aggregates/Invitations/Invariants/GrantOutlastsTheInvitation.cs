using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Tenancy;

public abstract partial class InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>
{
    /// <summary>The grant an invitation offers does not end before the invitation does.</summary>
    public sealed class GrantOutlastsTheInvitation : IInvariant<InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>>
    {
        /// <inheritdoc />
        public string Code => TenancyRefusals.InvitationGrantEndsFirst;

        /// <inheritdoc />
        public InvariantFailure? Check(InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId> entity)
            => entity.GrantUntil is not { } until || until > entity.ExpiresAt
                ? null
                : TenancyRefusals.Failure(TenancyRefusals.InvitationGrantEndsFirst);
    }
}
