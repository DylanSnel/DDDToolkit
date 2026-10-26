using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace DDDToolkit.Auth.Supabase;

/// <summary>
/// Checks a Supabase access token with the kind of key its header names. Supabase Auth signs a user's token
/// one of two ways: with the project's JWT secret, or with a signing key whose public half it publishes. A
/// project is on one of the two, and a developer's machine meets both under one issuer.
/// </summary>
/// <remarks>
/// <para>
/// A token's header says how it was signed, and that alone decides what its signature is checked with:
/// </para>
/// <list type="bullet">
/// <item><b>With the secret</b> (HS256): checked with the secrets among the signing keys of the validation
/// parameters, and with nothing else. Nothing is fetched, so a host with no Auth server in reach takes these
/// tokens all the same, and a host that was given no secret refuses them without asking anybody.</item>
/// <item><b>With a published key</b> (ES256 or RS256): checked with the keys Auth publishes at
/// <see cref="KeysAddress"/>, and with any public key among the parameters' signing keys. The published keys
/// are fetched when the first such token arrives and kept, as ASP.NET Core's JWT bearer keeps a provider's:
/// fetched again after <see cref="AutomaticRefreshInterval"/>, and when a token names a key that is not
/// among them, though not more often than once per <see cref="RefreshInterval"/>. That token is refused; one
/// that arrives after the keys were fetched again is checked with the new ones. With nobody to ask, the
/// token is refused.</item>
/// <item><b>Any other way</b>, or by nobody: refused, and nothing is fetched.</item>
/// </list>
/// <para>
/// Each kind allows its own algorithms only, narrowed further by
/// <see cref="TokenValidationParameters.ValidAlgorithms"/> where a host set them. So a published key, which
/// anybody can read, is never taken for a secret: a token that says it was signed with the secret is not held
/// against a published key at all, and a secret that Auth would publish by mistake is left out of the keys.
/// The issuer, the audience and the lifetime are checked the same either way, by the parameters themselves.
/// </para>
/// <para>
/// Parameters that carry a configuration manager of their own have the published keys come from there, and
/// <see cref="KeysAddress"/> is not asked.
/// </para>
/// <para>
/// <b>Whoever answers for the keys decides who is signed in.</b> So they are fetched over https. Plain http
/// is taken for an Auth server on this machine, <c>localhost</c> or a loopback address, where nothing
/// travels; for one on a private network of the host's own the host says so, with <c>allowPlainHttp</c>, as
/// <see cref="SupabaseAuthOptions.AllowPlainHttp"/> says it for the bearer and <see cref="SupabaseAuthAdmin"/>.
/// Any other address in plain http is refused when the handler is made.
/// </para>
/// <para>
/// <b>No token waits on Auth without end.</b> A request for the keys ends with the
/// <see cref="HttpClient.Timeout"/> of the client that fetches them, and a token that waits behind another
/// token's fetch stops waiting after as long. Either is refused, and says where the keys were asked for.
/// </para>
/// </remarks>
public sealed class SupabaseTokenHandler : TokenHandler
{
    private static readonly string[] WithTheSecret = [SecurityAlgorithms.HmacSha256];

    private static readonly string[] WithAPublishedKey = [SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.RsaSha256];

    private readonly JsonWebTokenHandler _tokens = new() { MapInboundClaims = false };
    private readonly HttpClient _http;
    private readonly HttpDocumentRetriever _documents;
    private readonly ConfigurationManager<PublishedKeys> _published;

    /// <summary>A handler for the tokens of the Auth server at <paramref name="authUrl"/>.</summary>
    /// <param name="authUrl">
    /// Where Auth answers: a project's <c>https://&lt;ref&gt;.supabase.co/auth/v1</c>, which
    /// <see cref="SupabaseTokens.IssuerOf"/> gives for the project's URL, or the address of an Auth server with
    /// no gateway in front of it.
    /// </param>
    /// <param name="httpClient">
    /// Fetches the published keys; a new one when left out. Its <see cref="HttpClient.Timeout"/> ends a
    /// request for them, and is also how long a token waits behind another token's fetch at most.
    /// </param>
    /// <param name="allowPlainHttp">
    /// Whether an <paramref name="authUrl"/> in plain http is taken for a server that is not on this machine:
    /// for an Auth server on a private network of the host's own. A token signed with a published key is
    /// checked with whatever key that address answers with, and unencrypted, whoever is in between could
    /// answer with a key of their own.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="authUrl"/> is not an absolute http or https URL, or is plain http to another machine
    /// without <paramref name="allowPlainHttp"/>.
    /// </exception>
    public SupabaseTokenHandler(string authUrl, HttpClient? httpClient = null, bool allowPlainHttp = false)
    {
        KeysAddress = SupabaseTokens.KeysAddressOf(authUrl, allowPlainHttp);
        _http = httpClient ?? new HttpClient();

        // In the clear only where the address is itself in the clear. Such an address got this far as one on
        // this machine, as a local stack's is, or as one the host said is on a network of its own.
        _documents = new HttpDocumentRetriever(_http)
        {
            RequireHttps = KeysAddress.StartsWith(Uri.UriSchemeHttps + "://", StringComparison.OrdinalIgnoreCase),
        };
        _published = new ConfigurationManager<PublishedKeys>(KeysAddress, new PublishedKeysRetriever(), _documents);
    }

    /// <summary>Where the published keys are fetched from: <c>{Auth}/.well-known/jwks.json</c>.</summary>
    public string KeysAddress { get; }

    /// <summary>
    /// Whether the published keys are fetched over https only. On unless the address the handler was made
    /// for is an http one, which it can be for an Auth server on this machine or with <c>allowPlainHttp</c>
    /// only; with it on, an http address is not asked and its tokens are refused. Off allows nothing the
    /// address does not: it is not the way to say that plain http to another machine is meant.
    /// </summary>
    public bool RequireHttps
    {
        get => _documents.RequireHttps;
        set => _documents.RequireHttps = value;
    }

    /// <summary>
    /// Whether a claim is renamed to the long name .NET has for it. Off: a claim keeps the name it has in the
    /// token, so <c>sub</c> is <c>sub</c>.
    /// </summary>
    public bool MapInboundClaims
    {
        get => _tokens.MapInboundClaims;
        set => _tokens.MapInboundClaims = value;
    }

    /// <summary>How long the published keys are kept before they are fetched again on their own.</summary>
    public TimeSpan AutomaticRefreshInterval
    {
        get => _published.AutomaticRefreshInterval;
        set => _published.AutomaticRefreshInterval = value;
    }

    /// <summary>
    /// The shortest time between two fetches a token asked for by naming a key that is not among the
    /// published ones. It is what keeps a stream of such tokens from becoming a stream of requests to Auth.
    /// </summary>
    public TimeSpan RefreshInterval
    {
        get => _published.RefreshInterval;
        set => _published.RefreshInterval = value;
    }

    /// <summary>
    /// Whether a token that names a key that is not among the published ones has them fetched again, which is
    /// how a new signing key is found before <see cref="AutomaticRefreshInterval"/> has passed. On.
    /// </summary>
    public bool RefreshOnKeyNotFound { get; set; } = true;

    /// <inheritdoc />
    public override SecurityToken ReadToken(string token) => _tokens.ReadToken(token);

    /// <inheritdoc />
    public override Task<TokenValidationResult> ValidateTokenAsync(SecurityToken token, TokenValidationParameters validationParameters)
        => token is JsonWebToken { EncodedToken: { Length: > 0 } encoded }
            ? ValidateTokenAsync(encoded, validationParameters)
            : Task.FromResult(Refused(new SecurityTokenMalformedException("Only a JSON Web Token as it was sent can be checked.")));

    /// <inheritdoc />
    public override async Task<TokenValidationResult> ValidateTokenAsync(string token, TokenValidationParameters validationParameters)
    {
        ArgumentNullException.ThrowIfNull(validationParameters);

        // The header picks the kind of key, and nothing more: whatever it says, the signature is then checked
        // with a key of that kind and an algorithm of that kind.
        string algorithm;
        try
        {
            algorithm = _tokens.ReadJsonWebToken(token).Alg;
        }
        catch (Exception unreadable)
        {
            return Refused(new SecurityTokenMalformedException("The token cannot be read, so it is nobody's.", unreadable));
        }

        var parameters = validationParameters.Clone();
        parameters.IssuerSigningKey = null;

        if (string.Equals(algorithm, SecurityAlgorithms.HmacSha256, StringComparison.Ordinal))
        {
            if (Narrowed(WithTheSecret, validationParameters) is not { Length: > 0 } secretAlgorithms)
            {
                return Refused(NotAllowed(algorithm));
            }

            // With the secret alone, and nobody is asked: not Auth, and not a configuration manager of the host's.
            parameters.ConfigurationManager = null;
            parameters.IssuerSigningKeys = [.. SigningKeysOf(validationParameters).Where(IsSecret)];
            parameters.ValidAlgorithms = secretAlgorithms;
            return await _tokens.ValidateTokenAsync(token, parameters).ConfigureAwait(false);
        }

        if (!WithAPublishedKey.Contains(algorithm, StringComparer.Ordinal) || Narrowed(WithAPublishedKey, validationParameters) is not { Length: > 0 } publishedAlgorithms)
        {
            return Refused(NotAllowed(algorithm));
        }

        var keys = SigningKeysOf(validationParameters).Where(key => !IsSecret(key)).ToList();
        var asksAuth = validationParameters.ConfigurationManager is null;
        Exception? unreachable = null;
        if (asksAuth)
        {
            try
            {
                // Not on a caller's cancellation, which a token handler is not given: the keys are fetched for
                // every token that follows too, and the request for them ends with the client's own timeout.
                // What ends with this token's patience is its wait behind the fetches other tokens started:
                // while Auth does not answer, each token in line tries in its turn, and the last would wait
                // for all of them. So a token waits that long behind the others and, where its turn comes
                // in time, that long again for its own request, and no longer.
                using var patience = new CancellationTokenSource(_http.Timeout);
                keys.AddRange((await _published.GetConfigurationAsync(patience.Token).ConfigureAwait(false)).Keys);
            }
            catch (Exception exception)
            {
                unreachable = exception;
            }
        }

        parameters.IssuerSigningKeys = keys;
        parameters.ValidAlgorithms = publishedAlgorithms;

        var result = await _tokens.ValidateTokenAsync(token, parameters).ConfigureAwait(false);
        if (result.IsValid || result.Exception is not SecurityTokenSignatureKeyNotFoundException || !asksAuth)
        {
            return result;
        }

        if (unreachable is not null)
        {
            return Refused(new SecurityTokenSignatureKeyNotFoundException(
                $"The keys Supabase Auth publishes at '{KeysAddress}' could not be fetched, so a token signed with one of them cannot be checked.", unreachable));
        }

        // Signed with a key that is not among the published ones: Auth may have a new one. This token stays
        // refused; the manager fetches again, no more often than its refresh interval allows.
        if (RefreshOnKeyNotFound)
        {
            _published.RequestRefresh();
        }

        return result;
    }

    /// <summary>The signing keys a host put on the parameters itself.</summary>
    private static IEnumerable<SecurityKey> SigningKeysOf(TokenValidationParameters parameters)
    {
        if (parameters.IssuerSigningKey is { } key)
        {
            yield return key;
        }

        foreach (var another in parameters.IssuerSigningKeys ?? [])
        {
            yield return another;
        }
    }

    /// <summary>Whether <paramref name="key"/> is a secret both sides hold, as opposed to the public half of a signing key.</summary>
    private static bool IsSecret(SecurityKey key)
        => key is SymmetricSecurityKey or JsonWebKey { Kty: JsonWebAlgorithmsKeyTypes.Octet };

    /// <summary>
    /// A kind's own algorithms, without the ones the host's parameters leave out. Empty when they leave out
    /// all of them, which the caller refuses: to the library underneath an empty list means any algorithm.
    /// </summary>
    private static string[] Narrowed(string[] own, TokenValidationParameters parameters)
        => parameters.ValidAlgorithms is { } valid && valid.Any()
            ? [.. own.Where(algorithm => valid.Contains(algorithm, StringComparer.Ordinal))]
            : own;

    private static SecurityTokenInvalidAlgorithmException NotAllowed(string? algorithm)
        => new("A Supabase access token is signed with HS256 and the project's JWT secret, or with ES256 or RS256 and a key Auth publishes. This one is signed another way, or a way this host does not allow.")
        {
            InvalidAlgorithm = algorithm,
        };

    private static TokenValidationResult Refused(Exception why) => new() { IsValid = false, Exception = why };

    /// <summary>The keys Auth publishes, as they were when they were last fetched.</summary>
    private sealed class PublishedKeys(IReadOnlyList<SecurityKey> keys)
    {
        public IReadOnlyList<SecurityKey> Keys => keys;
    }

    /// <summary>Reads the key set Auth publishes, which is the whole document.</summary>
    private sealed class PublishedKeysRetriever : IConfigurationRetriever<PublishedKeys>
    {
        public async Task<PublishedKeys> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancel)
        {
            var document = await retriever.GetDocumentAsync(address, cancel).ConfigureAwait(false);

            // Read into keys once per fetch, so every token is checked with the same key objects. Auth publishes
            // the public halves of its signing keys and never its secret; a secret that turned up here anyway
            // would be one the whole world can read, and is no key at all.
            return new PublishedKeys([.. new JsonWebKeySet(document).GetSigningKeys().Where(key => !IsSecret(key))]);
        }
    }
}
