namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>
/// The answer to inviting a person (<c>POST /tenancy/invitations</c>): the invitation, when it ends, and its
/// token, which the API answers this once and keeps nowhere.
/// </summary>
public sealed record IssuedInvitationInfo(Guid InvitationId, string Token, DateTimeOffset ExpiresAt)
{
    /// <summary>Says which invitation and not its token: a record prints its members, and a token must not end up in a log.</summary>
    public override string ToString() => nameof(IssuedInvitationInfo) + " " + InvitationId;
}
