using System.Buffers.Text;
using System.Security.Cryptography;

namespace Examples.Tenancy.AppHost;

/// <summary>
/// A signing key for an Auth server on a developer's machine, given to it the way Supabase's command line
/// tool gives one to the Auth server of the stack it starts. With it Auth signs a person's token with that
/// key (ES256) and publishes the key's public half, as a hosted project with signing keys does. Without it
/// Auth signs with its secret, and whoever checks a token has to hold that secret.
/// </summary>
/// <remarks>
/// <para>
/// The key is made up for one run and written down nowhere: the containers are new every run, and whoever
/// checks a token asks Auth for the public half. The secret stays beside it, since Auth still reads the
/// tokens it is sent with it: the service role's token for the admin API is signed with the secret.
/// </para>
/// <para>
/// This file is compiled into the test projects that start the same Auth image, so the tests and the AppHost
/// give Auth its key the same way.
/// </para>
/// </remarks>
public static class AuthSigningKeys
{
    /// <summary>The setting Auth reads its signing keys from: a JSON array of keys, private halves included.</summary>
    public const string Setting = "GOTRUE_JWT_KEYS";

    /// <summary>
    /// The setting that says how a token Auth is sent may be signed, under the name the pinned Auth image
    /// (<see cref="SupabaseImages.Auth"/>) reads. Supabase's tool sets it under a second spelling as well,
    /// without the last underscore, which this image does not read: given that one alone, it refuses the
    /// service role's token.
    /// </summary>
    public const string MethodsSetting = "GOTRUE_JWT_VALID_METHODS";

    /// <summary>
    /// With a key of its own, and with the secret still. Left to itself, an Auth server with signing keys
    /// reads only tokens signed the way those keys sign, and would refuse the service role's.
    /// </summary>
    public const string Methods = "HS256,RS256,ES256";

    /// <summary>A new P-256 key under an id of its own, as the array of one that <see cref="Setting"/> takes.</summary>
    public static string New()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(includePrivateParameters: true);

        return $$"""[{"kty":"EC","kid":"{{Guid.NewGuid()}}","use":"sig","key_ops":["sign","verify"],"alg":"ES256","ext":true,"crv":"P-256","x":"{{Base64Url.EncodeToString(parameters.Q.X)}}","y":"{{Base64Url.EncodeToString(parameters.Q.Y)}}","d":"{{Base64Url.EncodeToString(parameters.D)}}"}]""";
    }
}
