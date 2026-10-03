using System.Text.Json.Serialization;

namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>
/// An invitation that can still be accepted (<c>GET /tenancy/invitations</c>): the address it is for, the seat it
/// offers, placed in a unit with a role there until a moment or for good, and who issued it, by id. Never its token.
/// </summary>
public sealed record InvitationInfo(
    Guid Id,
    string Address,
    string? DisplayName,
    Guid UnitId,
    Guid RoleId,
    [property: JsonPropertyName("until")] DateTimeOffset? RoleEndsAt,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    Guid? IssuedBy);
