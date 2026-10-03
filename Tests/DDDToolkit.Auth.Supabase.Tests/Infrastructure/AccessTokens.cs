using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DDDToolkit.Auth.Supabase.Tests.Infrastructure;

/// <summary>
/// Access tokens as Supabase Auth signs them for a signed-in user, made by the test itself with whatever key
/// and in whatever way the test is about: the two kinds a project has, and the ones nobody should take.
/// </summary>
internal static class AccessTokens
{
    /// <summary>A project's URL, in a domain nobody can register.</summary>
    public const string ProjectUrl = "https://project.example.test";

    /// <summary>What the project's tokens name as their issuer, and where its Auth answers.</summary>
    public const string Issuer = StubAuthServer.Url;

    /// <summary>Where the project's Auth publishes its keys.</summary>
    public const string KeysAddress = Issuer + "/.well-known/jwks.json";

    /// <summary>Stands in for the project's JWT secret. It opens nothing.</summary>
    public const string Secret = "a-jwt-secret-for-the-token-tests-of-at-least-32-characters";

    /// <summary>The user the tokens are for, unless a test says otherwise.</summary>
    public static readonly Guid Ada = Guid.Parse("8051a7e8-8599-4ad8-b169-68dc96beb0c5");

    /// <summary><paramref name="secret"/> as the key a token is signed and checked with.</summary>
    public static SymmetricSecurityKey SecretKey(string secret = Secret) => new(Encoding.UTF8.GetBytes(secret));

    /// <summary>A new P-256 signing key, as a project's asymmetric signing keys are.</summary>
    public static ECDsaSecurityKey NewSigningKey(string id) => new(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = id };

    /// <summary>A new RSA signing key, the other kind a project can sign with.</summary>
    public static RsaSecurityKey NewRsaSigningKey(string id) => new(RSA.Create(2048)) { KeyId = id };

    /// <summary>A token for <see cref="Ada"/> signed with the secret (HS256).</summary>
    public static string SignedWithTheSecret(string secret = Secret)
        => For(new SigningCredentials(SecretKey(secret), SecurityAlgorithms.HmacSha256));

    /// <summary>A token for <see cref="Ada"/> signed with <paramref name="key"/>, the way such a key signs for a project unless <paramref name="algorithm"/> says another.</summary>
    public static string SignedWith(SecurityKey key, string? algorithm = null)
        => For(new SigningCredentials(key, algorithm ?? (key is RsaSecurityKey ? SecurityAlgorithms.RsaSha256 : SecurityAlgorithms.EcdsaSha256)));

    /// <summary>
    /// A token as Supabase Auth issues one, unless an argument says otherwise: signed with
    /// <paramref name="signedWith"/>, or by nobody when there is none.
    /// </summary>
    public static string For(SigningCredentials? signedWith, Guid? user = null, string issuer = Issuer, string audience = SupabaseTokens.Audience, bool endedAnHourAgo = false)
    {
        var issued = endedAnHourAgo ? DateTime.UtcNow.AddHours(-2) : DateTime.UtcNow;
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = issued,
            Expires = issued.AddHours(1),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = (user ?? Ada).ToString(),
                ["role"] = "authenticated",
                ["email"] = "ada.lindqvist@example.test",
                ["app_metadata"] = new Dictionary<string, object> { ["provider"] = "email" },
            },
            SigningCredentials = signedWith,
        });
    }
}
