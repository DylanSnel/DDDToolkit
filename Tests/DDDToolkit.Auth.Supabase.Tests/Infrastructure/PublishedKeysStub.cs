using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace DDDToolkit.Auth.Supabase.Tests.Infrastructure;

/// <summary>
/// Stands in for the place Supabase Auth publishes its keys: the handler the keys are fetched through. It
/// answers every request with the key set the test gave it last, and keeps the address of each request, so a
/// test says what Auth publishes and reads back how often, and where, it was asked. No server and no network.
/// </summary>
/// <remarks>
/// Keys that are fetched again are fetched behind the token that asked for them, on a thread of the library's
/// own, so a test cannot await that fetch. Each request therefore signals its arrival, and
/// <see cref="IsAskedAsync"/> waits on that signal, where a pause and a look afterwards would miss a request
/// that is still on its way.
/// </remarks>
internal sealed class PublishedKeysStub : HttpMessageHandler
{
    /// <summary>The path of the discovery document under an Auth server's address.</summary>
    public const string DiscoveryPath = "/.well-known/openid-configuration";

    private readonly SemaphoreSlim _arrived = new(0);
    private volatile string _document = """{"keys":[]}""";
    private volatile bool _down;
    private volatile TaskCompletionSource? _silence;

    /// <summary>Where the keys were asked for, in order.</summary>
    public ConcurrentQueue<Uri> Asked { get; } = new();

    /// <summary>How often the keys were fetched, or tried to be.</summary>
    public int Fetches => Asked.Count;

    /// <summary>A client that fetches through this.</summary>
    public HttpClient Client() => new(this, disposeHandler: false);

    /// <summary>
    /// Whether the keys were asked for <paramref name="times"/> times in all: at once when they were already,
    /// and otherwise the moment the request that makes it so arrives, or not within <paramref name="within"/>.
    /// </summary>
    /// <remarks>
    /// For a fetch that has to come, the time is a limit that is never reached. For one that must not, it is
    /// how long the test listens: a request that arrives in that time fails the test at once, however busy the
    /// machine is, where a look at <see cref="Fetches"/> after a pause would have missed one still on its way.
    /// </remarks>
    public async Task<bool> IsAskedAsync(int times, TimeSpan within, CancellationToken cancellation)
    {
        var listening = Stopwatch.StartNew();
        while (Fetches < times)
        {
            // A signal can be one of an earlier request, so each one is only a reason to count again.
            var left = within - listening.Elapsed;
            if (left <= TimeSpan.Zero || !await _arrived.WaitAsync(left, cancellation))
            {
                return Fetches >= times;
            }
        }

        return true;
    }

    /// <summary>Auth publishes the public halves of <paramref name="keys"/>, and nothing else, from now on.</summary>
    public PublishedKeysStub Publishes(params SecurityKey[] keys)
        => PublishesDocument("{\"keys\":[" + string.Join(",", keys.Select(PublicHalfOf)) + "]}");

    /// <summary>Auth answers with <paramref name="document"/> from now on, whatever it is.</summary>
    public PublishedKeysStub PublishesDocument(string document)
    {
        _document = document;
        _down = false;
        return this;
    }

    /// <summary>Nobody answers from now on: the connection fails.</summary>
    public PublishedKeysStub IsDown()
    {
        _down = true;
        return this;
    }

    /// <summary>
    /// From now on a request arrives and gets no answer, until <see cref="AnswersAgain"/>: Auth takes the
    /// connection and says nothing. The request stays open however often it is cancelled, as one does that
    /// is stuck where no timeout reaches, so a test decides when it ends.
    /// </summary>
    public PublishedKeysStub SaysNothing()
    {
        _silence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return this;
    }

    /// <summary>The requests that were left without an answer get theirs, and so does every later one.</summary>
    public PublishedKeysStub AnswersAgain()
    {
        var silence = _silence;
        _silence = null;
        silence?.TrySetResult();
        return this;
    }

    /// <summary>
    /// A secret as a key set would list it. Auth never publishes one; a test publishes it to see that it is
    /// left out all the same.
    /// </summary>
    public static string SecretAsPublished(string id, byte[] secret)
        => $$"""{"kty":"oct","alg":"HS256","use":"sig","kid":"{{id}}","k":"{{Base64UrlEncoder.Encode(secret)}}"}""";

    /// <summary>A key as a key set lists it: its public half only.</summary>
    public static string PublicHalfOf(SecurityKey key) => key switch
    {
        ECDsaSecurityKey elliptic => PublicHalfOf(elliptic.KeyId, elliptic.ECDsa.ExportParameters(includePrivateParameters: false)),
        RsaSecurityKey rsa => PublicHalfOf(rsa.KeyId, rsa.Rsa.ExportParameters(includePrivateParameters: false)),
        _ => throw new ArgumentException($"A {key.GetType().Name} has no public half to publish.", nameof(key)),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Asked.Enqueue(request.RequestUri!);
        _arrived.Release();

        if (_silence is { } silence)
        {
            await silence.Task;
        }

        if (_down)
        {
            throw new HttpRequestException("Nobody answers at " + request.RequestUri!.GetLeftPart(UriPartial.Authority) + ".");
        }

        // A host that asks for a discovery document, as a scheme with an authority of its own does, is sent to
        // the key set beside it.
        var address = request.RequestUri!.GetLeftPart(UriPartial.Path);
        var answer = address.EndsWith(DiscoveryPath, StringComparison.Ordinal)
            ? $$"""{"issuer":"{{address[..^DiscoveryPath.Length]}}","jwks_uri":"{{address[..^DiscoveryPath.Length]}}/.well-known/jwks.json"}"""
            : _document;

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
    }

    private static string PublicHalfOf(string id, ECParameters key)
    {
        var (curve, algorithm) = key.Q.X!.Length switch
        {
            32 => ("P-256", "ES256"),
            48 => ("P-384", "ES384"),
            _ => throw new ArgumentException("A curve these tests do not use."),
        };

        return $$"""{"kty":"EC","crv":"{{curve}}","alg":"{{algorithm}}","use":"sig","key_ops":["verify"],"kid":"{{id}}","x":"{{Base64UrlEncoder.Encode(key.Q.X)}}","y":"{{Base64UrlEncoder.Encode(key.Q.Y)}}"}""";
    }

    private static string PublicHalfOf(string id, RSAParameters key)
        => $$"""{"kty":"RSA","alg":"RS256","use":"sig","key_ops":["verify"],"kid":"{{id}}","n":"{{Base64UrlEncoder.Encode(key.Modulus)}}","e":"{{Base64UrlEncoder.Encode(key.Exponent)}}"}""";
}
