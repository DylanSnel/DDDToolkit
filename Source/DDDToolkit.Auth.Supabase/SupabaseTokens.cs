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
