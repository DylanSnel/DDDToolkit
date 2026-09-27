using System.Security.Cryptography;
using System.Text;
using DDDToolkit.Auth.Supabase;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>Access tokens as Supabase Auth signs them, for the tests of everything that reads one.</summary>
public static class SupabaseTestTokens
{
    /// <summary>The local stack's URL, which is also what the tests call their project.</summary>
    public const string LocalProjectUrl = "http://127.0.0.1:54321";

    /// <summary>What <c>supabase status</c> prints as the JWT secret of a local stack.</summary>
    public const string LocalJwtSecret = "super-secret-jwt-token-with-at-least-32-characters-long";

    /// <summary>A token signed the way the local stack signs one: HS256 with its JWT secret.</summary>
    public static string Local(Guid user, string secret = LocalJwtSecret, string? issuer = null, DateTime? expires = null)
        => Create(user, new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256, issuer ?? LocalProjectUrl + "/auth/v1", expires);

    /// <summary>A token signed the way a hosted project signs one: ES256 with a key it publishes.</summary>
    public static string Signed(Guid user, ECDsaSecurityKey key, string issuer, DateTime? expires = null, string audience = SupabaseTokens.Audience)
        => Create(user, key, SecurityAlgorithms.EcdsaSha256, issuer, expires, audience);

    /// <summary>A new P-256 signing key, as a project's asymmetric signing keys are.</summary>
    public static ECDsaSecurityKey NewSigningKey(string id) => new(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = id };

    /// <summary>The key as a JSON Web Key Set lists it: public parts only.</summary>
    public static string JwksOf(params ECDsaSecurityKey[] keys)
        => "{\"keys\":[" + string.Join(",", keys.Select(key =>
        {
            var jwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(key);
            return $$"""{"kty":"EC","crv":"P-256","alg":"ES256","use":"sig","kid":"{{key.KeyId}}","x":"{{jwk.X}}","y":"{{jwk.Y}}"}""";
        })) + "]}";

    private static string Create(Guid user, SecurityKey key, string algorithm, string issuer, DateTime? expires, string audience = SupabaseTokens.Audience)
    {
        var end = expires ?? DateTime.UtcNow.AddHours(1);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = end.AddHours(-1),
            NotBefore = end.AddHours(-1),
            Expires = end,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = user.ToString(),
                ["role"] = "authenticated",
                ["email"] = $"{user:N}@example.com",
                ["app_metadata"] = new Dictionary<string, object> { ["provider"] = "email", ["teams"] = new[] { "north" } },
            },
            SigningCredentials = new SigningCredentials(key, algorithm),
        });
    }
}
