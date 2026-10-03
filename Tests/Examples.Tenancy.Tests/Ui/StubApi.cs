using System.Net;
using System.Text;

namespace Examples.Tenancy.Tests.Ui;

/// <summary>A request as the stub received it, read before the client disposed it.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="PathAndQuery">The path and query.</param>
/// <param name="Authorization">The <c>Authorization</c> header, or <see langword="null"/>.</param>
/// <param name="Tenant">The <c>Tenant</c> header, or <see langword="null"/>.</param>
/// <param name="Body">The body, or <see langword="null"/>.</param>
public sealed record StubRequest(string Method, string PathAndQuery, string? Authorization, string? Tenant, string? Body);

/// <summary>
/// An API that answers what the test tells it to and remembers every request, so the UI's client can be tested as
/// plain C#: no host, no browser.
/// </summary>
public sealed class StubApi : HttpMessageHandler
{
    /// <summary>Where the stub pretends to be.</summary>
    public static readonly Uri BaseAddress = new("http://api.test/");

    private readonly Func<StubRequest, HttpResponseMessage> _answer;
    private readonly List<StubRequest> _requests = [];

    /// <summary>A stub that answers every request with <paramref name="answer"/>'s response.</summary>
    public StubApi(Func<StubRequest, HttpResponseMessage> answer) => _answer = answer;

    /// <summary>Every request received, in order.</summary>
    public IReadOnlyList<StubRequest> Requests => _requests;

    /// <summary>A stub that answers every request with <paramref name="status"/> and <paramref name="body"/>.</summary>
    public static StubApi Answering(HttpStatusCode status, string? body = null, string mediaType = "application/json")
        => new(_ => Response(status, body, mediaType));

    /// <summary>A response with <paramref name="status"/> and, when given, <paramref name="body"/>.</summary>
    public static HttpResponseMessage Response(HttpStatusCode status, string? body = null, string mediaType = "application/json")
    {
        var response = new HttpResponseMessage(status);
        if (body is not null)
        {
            response.Content = new StringContent(body, Encoding.UTF8, mediaType);
        }

        return response;
    }

    /// <summary>A client over this stub, as the UI's typed client is given one.</summary>
    public HttpClient Client() => new(this, disposeHandler: false) { BaseAddress = BaseAddress };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var received = new StubRequest(
            request.Method.Method,
            request.RequestUri!.PathAndQuery,
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("Tenant", out var tenants) ? string.Join(",", tenants) : null,
            request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));

        lock (_requests)
        {
            _requests.Add(received);
        }

        return _answer(received);
    }
}
