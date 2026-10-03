using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DDDToolkit.Auth.Supabase;

/// <summary>What every Supabase Auth access token has in common, for the validator and the host packages alike.</summary>
public static class SupabaseTokens
{
    /// <summary>The audience of every access token Supabase Auth issues to a signed-in user.</summary>
    public const string Audience = "authenticated";

    /// <summary>
    /// <c>{projectUrl}/auth/v1</c>, the issuer of a project's tokens, for the project's URL,
    /// <c>https://&lt;ref&gt;.supabase.co</c>, or the local stack's, <c>http://127.0.0.1:54321</c>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="projectUrl"/> is not an absolute http or https URL.</exception>
    public static string IssuerOf(string projectUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectUrl);

        if (!Uri.TryCreate(projectUrl.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException($"'{projectUrl}' is not a Supabase project URL. Pass https://<ref>.supabase.co, or the local stack's http://127.0.0.1:54321.", nameof(projectUrl));
        }

        var root = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return root.EndsWith("/auth/v1", StringComparison.OrdinalIgnoreCase) ? root : root + "/auth/v1";
    }

    /// <summary>
    /// <c>{authUrl}/.well-known/jwks.json</c>, where an Auth server publishes the public halves of the keys
    /// it signs with, for the address Auth answers at: a project's <c>https://&lt;ref&gt;.supabase.co/auth/v1</c>,
    /// which <see cref="IssuerOf"/> gives, or that of an Auth server with no gateway in front of it.
    /// </summary>
    /// <remarks>
    /// The keys are what every token signed with one of them is checked with, so whoever answers this address
    /// decides who is signed in. Over https that is Auth. Plain http is taken for an Auth server on this
    /// machine, <c>localhost</c> or a loopback address, where nothing travels; to another machine anybody in
    /// between could answer with a key of their own, so there the host says that the network in between is
    /// its own, with <paramref name="allowPlainHttp"/>. It is the rule <see cref="SupabaseAuthAdmin"/> has
    /// for the secret key.
    /// </remarks>
    /// <param name="authUrl">Where Auth answers.</param>
    /// <param name="allowPlainHttp">
    /// Whether an <paramref name="authUrl"/> in plain http is taken for a server that is not on this machine:
    /// for an Auth server on a private network of the host's own.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="authUrl"/> is not an absolute http or https URL, or is plain http to another machine
    /// without <paramref name="allowPlainHttp"/>.
    /// </exception>
    public static string KeysAddressOf(string authUrl, bool allowPlainHttp = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authUrl);

        if (!Uri.TryCreate(authUrl.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException($"'{authUrl}' is not where a Supabase Auth server answers. Pass https://<ref>.supabase.co/auth/v1, or the address of an Auth server of your own.", nameof(authUrl));
        }

        // The address may be the project's own, where the host gave no other, so the message names neither
        // setting. It does not repeat the address: one pasted from a connection setting can have a password in it.
        if (IsPlainHttpToAnotherMachine(uri) && !allowPlainHttp)
        {
            throw new ArgumentException(
                "The address Supabase Auth answers at is plain http to another machine, so the keys a token is checked with would be fetched unencrypted, and whoever is in between could answer with a key of their own. Use https. " +
                "For an Auth server on a private network of your own, say so: SupabaseAuthOptions.AllowPlainHttp, or allowPlainHttp: true.",
                nameof(authUrl));
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/.well-known/jwks.json";
    }

    /// <summary>
    /// Whether what is sent to <paramref name="address"/> travels unencrypted over a network: plain http to
    /// anything but <c>localhost</c> or a loopback address. One rule for the secret key the admin client
    /// sends and for the keys the token handler fetches.
    /// </summary>
    internal static bool IsPlainHttpToAnotherMachine(Uri address) => address.Scheme == Uri.UriSchemeHttp && !address.IsLoopback;

    /// <summary>
    /// The payload of a validated token, decoded but otherwise exactly as it was signed, so the claims
    /// Postgres gets are the ones PostgREST would have given it, nested ones included.
    /// <see langword="null"/> for a token of another kind.
    /// </summary>
    public static string? ClaimsOf(SecurityToken? token) => token switch
    {
        JsonWebToken jwt => Base64UrlEncoder.Decode(jwt.EncodedPayload),
        JwtSecurityToken jwt => Base64UrlEncoder.Decode(jwt.RawPayload),
        _ => null,
    };

    /// <summary>
    /// The token in an <c>Authorization</c> header, <c>Bearer &lt;token&gt;</c>, or <see langword="null"/>
    /// when there is none.
    /// </summary>
    public static string? BearerOf(string? authorization)
    {
        const string Scheme = "Bearer ";
        return authorization is { Length: > 7 } && authorization.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)
            ? authorization[Scheme.Length..].Trim() is { Length: > 0 } token ? token : null
            : null;
    }
}
