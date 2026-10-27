namespace Examples.Tenancy.Tenants.Application.Invitations;

/// <summary>An invitation as inviting a person answers it: the one time its token is shown.</summary>
/// <param name="Id">The invitation.</param>
/// <param name="Token">
/// The token whoever accepts needs. Send it to the person, in the fragment of a link or in a message's text, and keep it
/// nowhere.
/// </param>
/// <param name="ExpiresAt">The first moment it can no longer be accepted.</param>
public sealed record IssuedInvitationListing(InvitationId Id, string Token, DateTimeOffset ExpiresAt)
{
    /// <summary>Says which invitation and not its token: a record prints its members, and this one must not end up in a log.</summary>
    public override string ToString() => nameof(IssuedInvitationListing) + " " + Id;
}
