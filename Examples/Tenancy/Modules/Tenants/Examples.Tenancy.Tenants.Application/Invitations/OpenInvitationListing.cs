using Examples.Tenancy.Tenants.Domain.Aggregates.Invitations;

namespace Examples.Tenancy.Tenants.Application.Invitations;

/// <summary>
/// An invitation that can still be accepted, as the people who manage seats at its unit see it: what this application
/// selects of the package's invitation. It holds no token: the store keeps the token's digest apart.
/// </summary>
/// <param name="Id">The invitation.</param>
/// <param name="Address">The address it is for.</param>
/// <param name="UnitId">The unit the seat would be placed in.</param>
/// <param name="RoleId">The role the seat would hold there.</param>
/// <param name="GrantUntil">When that grant would end, or <see langword="null"/> for no end.</param>
/// <param name="IssuedAt">When it was issued.</param>
/// <param name="ExpiresAt">The first moment it can no longer be accepted.</param>
/// <param name="IssuedBy">The seat that issued it, or that system work issued it for; <see langword="null"/> for none.</param>
/// <param name="IssuedAsSystem">Whether system work issued it.</param>
public sealed record OpenInvitationListing(
    InvitationId Id,
    string Address,
    OrganizationUnitId UnitId,
    RoleId RoleId,
    DateTimeOffset? GrantUntil,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    SeatId? IssuedBy,
    bool IssuedAsSystem)
{
    /// <summary>What this application shows of <paramref name="invitation"/>.</summary>
    /// <param name="invitation">An open invitation, as the package's invitation manager answered it.</param>
    internal static OpenInvitationListing From(Invitation invitation)
        => new(
            invitation.Id,
            invitation.Address ?? string.Empty,
            invitation.UnitId,
            invitation.RoleId,
            invitation.GrantUntil,
            invitation.IssuedAt,
            invitation.ExpiresAt,
            invitation.IssuedBy,
            invitation.IssuedAsSystem);
}
