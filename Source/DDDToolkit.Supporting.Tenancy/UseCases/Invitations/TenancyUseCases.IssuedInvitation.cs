using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>An invitation as <c>IssueAsync</c> returns it: the one time its token is shown.</summary>
    /// <param name="Id">The invitation.</param>
    /// <param name="Token">
    /// The token whoever accepts needs. Send it to the person, in the fragment of a link or in a message's text,
    /// and keep it nowhere.
    /// </param>
    /// <param name="ExpiresAt">The first moment it can no longer be accepted.</param>
    public sealed record IssuedInvitation<TInvitationId>(TInvitationId Id, string Token, DateTimeOffset ExpiresAt)
        where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
    {
        /// <summary>Says which invitation and not its token: a record prints its members, and this one must not end up in a log.</summary>
        public override string ToString() => nameof(IssuedInvitation<TInvitationId>) + " " + Id;
    }
}
