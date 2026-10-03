using System.Net;
using System.Text;
using System.Text.Json;

namespace DDDToolkit.Auth.Supabase.Tests.Infrastructure;

/// <summary>
/// Stands in for Supabase Auth: the handler the admin client sends through. It keeps every request as it
/// arrived and answers each with the next answer the test queued, so a test says exactly what Auth does
/// and reads back exactly what was sent, without a server or a network.
/// </summary>
internal sealed class StubAuthServer : HttpMessageHandler
{
    /// <summary>A project's Auth URL, in a domain nobody can register.</summary>
    public const string Url = "https://project.example.test/auth/v1";

    /// <summary>Stands in for the project's secret key. Not shaped like a real one, so nothing mistakes it for a leak.</summary>
    public const string SecretKey = "the-secret-key-of-these-tests";

    private readonly Queue<Func<HttpResponseMessage>> _answers = new();

    /// <summary>Every request that arrived, in order.</summary>
    public List<SentRequest> Requests { get; } = [];

    /// <summary>The one request a test expects to have been sent.</summary>
    public SentRequest Request => Requests.Count == 1
        ? Requests[0]
        : throw new InvalidOperationException($"Expected one request, but {Requests.Count} arrived.");

    /// <summary>An admin client that talks to this instead of a project.</summary>
    public SupabaseAuthAdmin Admin(string url = Url, bool allowPlainHttp = false) => new(url, SecretKey, this, allowPlainHttp);

    /// <summary>Queues an answer with a status and a JSON body.</summary>
    public StubAuthServer Answers(int status, string json)
        => Answers(status, json, "application/json");

    /// <summary>Queues an answer with a status and a body of any kind, such as a gateway's page.</summary>
    public StubAuthServer Answers(int status, string body, string mediaType)
    {
        _answers.Enqueue(() => new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, mediaType) });
        return this;
    }

    /// <summary>Queues a request that never gets an answer: the connection fails.</summary>
    public StubAuthServer Fails(Exception failure)
        => Fails(() => failure);

    /// <summary>
    /// Queues a request that never gets an answer, with a failure made when the request arrives, for a test
    /// in which something else happens at that moment, such as the caller giving up.
    /// </summary>
    public StubAuthServer Fails(Func<Exception> failure)
    {
        _answers.Enqueue(() => throw failure());
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers
            .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(header => header.Key, header => string.Join(", ", header.Value), StringComparer.OrdinalIgnoreCase);
        Requests.Add(new SentRequest(request.Method, request.RequestUri!, headers, body));

        return _answers.Count > 0
            ? _answers.Dequeue()()
            : throw new InvalidOperationException($"The test queued no answer for {request.Method} {request.RequestUri}.");
    }
}

/// <summary>A request as it reached <see cref="StubAuthServer"/>.</summary>
/// <param name="Method">Its method.</param>
/// <param name="Uri">Where it went, query included.</param>
/// <param name="Headers">Its headers and its content's, by name.</param>
/// <param name="Body">Its body as text, or <see langword="null"/> without one.</param>
internal sealed record SentRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string? Body)
{
    /// <summary>The body as JSON, for a test that reads what was sent member by member.</summary>
    public JsonElement Json => JsonDocument.Parse(Body ?? throw new InvalidOperationException("The request had no body.")).RootElement;

    /// <summary>The names of the body's members, in the order they were written.</summary>
    public IReadOnlyList<string> Members => [.. Json.EnumerateObject().Select(member => member.Name)];
}
