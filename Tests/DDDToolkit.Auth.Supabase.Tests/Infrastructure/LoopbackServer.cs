using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DDDToolkit.Auth.Supabase.Tests.Infrastructure;

/// <summary>
/// A server on this machine's loopback address that gives every request the same answer and keeps each
/// request as it was on the wire, request line, headers and body. It is for the few tests about the client
/// the admin client makes for itself, which <see cref="StubAuthServer"/> replaces: whether a redirect is
/// followed is decided there and nowhere else.
/// </summary>
internal sealed class LoopbackServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, port: 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly string _answer;
    private readonly Task _serving;

    /// <summary>Starts a server that answers everything with <paramref name="answer"/>, a whole HTTP response.</summary>
    public LoopbackServer(string answer)
    {
        _answer = answer;
        _listener.Start();
        _serving = ServeAsync();
    }

    /// <summary>Where the server listens, without a path.</summary>
    public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    /// <summary>Every request that arrived, as the text that was sent.</summary>
    public ConcurrentQueue<string> Requests { get; } = new();

    /// <summary>An HTTP response with a JSON body, after which the server closes the connection.</summary>
    /// <param name="status">The status.</param>
    /// <param name="body">The body.</param>
    /// <param name="headers">Further header lines, such as <c>Location: ...</c>.</param>
    public static string Answer(int status, string body, params string[] headers)
    {
        var response = new StringBuilder()
            .Append("HTTP/1.1 ").Append(status).Append(" Answer\r\n")
            .Append("Content-Type: application/json\r\n")
            .Append("Content-Length: ").Append(Encoding.UTF8.GetByteCount(body)).Append("\r\n")
            .Append("Connection: close\r\n");
        foreach (var header in headers)
        {
            response.Append(header).Append("\r\n");
        }

        return response.Append("\r\n").Append(body).ToString();
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _listener.Stop();
        await _serving;
        _stopping.Dispose();
    }

    private async Task ServeAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                await using var stream = client.GetStream();

                var request = await ReadRequestAsync(stream, _stopping.Token);
                if (request.Length == 0)
                {
                    continue;
                }

                Requests.Enqueue(request);
                await stream.WriteAsync(Encoding.UTF8.GetBytes(_answer), _stopping.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception failure) when (failure is IOException or SocketException or ObjectDisposedException)
            {
                // One connection that broke, or the listener stopping: neither is the next request's problem.
            }
        }
    }

    /// <summary>Reads one request: the headers up to the empty line, then as many bytes as Content-Length says.</summary>
    private static async Task<string> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        const string EndOfHeaders = "\r\n\r\n";

        var received = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var text = Encoding.UTF8.GetString(received.GetBuffer(), 0, (int)received.Length);
            var headersEnd = text.IndexOf(EndOfHeaders, StringComparison.Ordinal);
            if (headersEnd >= 0)
            {
                var bodyStart = Encoding.UTF8.GetByteCount(text[..(headersEnd + EndOfHeaders.Length)]);
                if (received.Length - bodyStart >= ContentLengthOf(text[..headersEnd]))
                {
                    return text;
                }
            }

            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return text;
            }

            received.Write(buffer, 0, read);
        }
    }

    private static int ContentLengthOf(string headers)
    {
        const string Name = "Content-Length:";

        foreach (var line in headers.Split("\r\n"))
        {
            if (line.StartsWith(Name, StringComparison.OrdinalIgnoreCase))
            {
                return int.Parse(line[Name.Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return 0;
    }
}
