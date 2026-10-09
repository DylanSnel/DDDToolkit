using System.Text;
using DDDToolkit.Auth.Supabase;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Examples.Tenancy.Host.DevLogin;

/// <summary>
/// Signs access tokens for the demonstration people the way the local Supabase stack signs them, so the API
/// checks them with the same bearer, the same issuer and the same secret it would check a real one with.
/// Nothing in the API knows a token came from here.
/// </summary>
/// <remarks>
/// The claims are the ones Supabase Auth puts in a user's access token: <c>iss</c>, <c>aud</c>
/// (<c>authenticated</c>), <c>sub</c>, <c>role</c>, <c>email</c>, <c>aal</c>, <c>session_id</c>,
/// <c>is_anonymous</c>, <c>app_metadata</c>, <c>user_metadata</c>, <c>iat</c> and <c>exp</c>, an hour
/// later. Supabase writes no <c>nbf</c>, and nor does this.
/// </remarks>
/// <param name="projectUrl">The local stack's URL; the issuer is derived from it as Supabase does.</param>
/// <param name="jwtSecret">The local stack's JWT secret, which the bearer checks the signature with.</param>
/// <param name="clock">What "now" is.</param>
public sealed class LocalTokenIssuer(string projectUrl, string jwtSecret, TimeProvider clock)
{
    /// <summary>How long a token is accepted, as Supabase's default.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    /// <summary>The database role Supabase gives a signed-in user, and the role the tokens claim.</summary>
    public const string Role = "authenticated";

    private readonly string _issuer = SupabaseTokens.IssuerOf(projectUrl);

    private readonly SigningCredentials _signing = new(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret ?? throw new ArgumentNullException(nameof(jwtSecret)))),
        SecurityAlgorithms.HmacSha256);

    /// <summary>A token for <paramref name="person"/> that claims <paramref name="role"/>, issued now.</summary>
    public IssuedToken Issue(DemoPerson person, string role = Role) => IssueAt(person, clock.GetUtcNow(), role);

    /// <summary>
    /// A token for <paramref name="person"/>, issued at <paramref name="issuedAt"/>: one issued more than
    /// <see cref="Lifetime"/> ago has expired, which is how a test gets an expired token.
    /// </summary>
    /// <param name="person">Whom the token is for.</param>
    /// <param name="issuedAt">When it is issued.</param>
    /// <param name="role">
    /// The role the token claims: a signed-in user's, or the operators', which Supabase Auth writes for a member of
    /// staff the project's owner marked as one.
    /// </param>
    public IssuedToken IssueAt(DemoPerson person, DateTimeOffset issuedAt, string role = Role)
    {
        ArgumentNullException.ThrowIfNull(person);
        ArgumentException.ThrowIfNullOrEmpty(role);

        // A token counts in whole seconds, so the answer does too.
        issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAt.ToUnixTimeSeconds());
        var expiresAt = issuedAt + Lifetime;
        var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _issuer,
            Audience = SupabaseTokens.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = person.Id.ToString(),
                ["role"] = role,
                ["email"] = person.Email,
                ["aal"] = "aal1",
                ["session_id"] = Guid.NewGuid().ToString(),
                ["is_anonymous"] = false,
                ["app_metadata"] = new Dictionary<string, object> { ["provider"] = "email", ["providers"] = new[] { "email" } },
                ["user_metadata"] = new Dictionary<string, object> { ["email"] = person.Email, ["email_verified"] = true, ["name"] = person.Name },
            },
            SigningCredentials = _signing,
        });

        return new IssuedToken(token, expiresAt);
    }
}
