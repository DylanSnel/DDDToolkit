using System.Text.Json.Serialization;

namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>
/// The dev login's answer (<c>POST /dev/auth/token</c>), in the shape of Supabase Auth's token endpoint, so a
/// client written for one reads the other.
/// </summary>
public sealed record TokenAnswer(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_at")] long ExpiresAt,
    [property: JsonPropertyName("user")] TokenUser? User)
{
    /// <summary>When the token stops being accepted.</summary>
    public DateTimeOffset Expires => DateTimeOffset.FromUnixTimeSeconds(ExpiresAt);
}
