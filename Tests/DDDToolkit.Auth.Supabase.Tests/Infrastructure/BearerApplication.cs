using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using DDDToolkit.Auth.Supabase.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DDDToolkit.Auth.Supabase.Tests.Infrastructure;

/// <summary>
/// An ASP.NET Core application of the test's own, on Kestrel on this machine, with the Supabase bearer scheme
/// the test set up and two routes: one that asks who is calling and needs a user, and one that answers
/// anybody. A request goes the whole way a host's goes, from the header to the caller.
/// </summary>
internal sealed class BearerApplication : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly CancellationToken _cancellation;

    private BearerApplication(WebApplication app, HttpClient client, ConcurrentQueue<Exception> refusals, CancellationToken cancellation)
    {
        _app = app;
        _client = client;
        _cancellation = cancellation;
        Refusals = refusals;
    }

    /// <summary>Why each token that was refused was refused, in order.</summary>
    public ConcurrentQueue<Exception> Refusals { get; }

    /// <summary>The scheme's options as the host ends up with them.</summary>
    public JwtBearerOptions Options(string scheme = JwtBearerDefaults.AuthenticationScheme)
        => _app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(scheme);

    /// <summary>Starts an application whose default scheme is the one <paramref name="scheme"/> adds.</summary>
    public static async Task<BearerApplication> StartAsync(Action<AuthenticationBuilder> scheme, CancellationToken cancellation)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        scheme(builder.Services.AddAuthentication());
        builder.Services.AddAuthorization();

        // After the scheme's own set-up, so a refusal is written down whatever the scheme does with it.
        var refusals = new ConcurrentQueue<Exception>();
        builder.Services.PostConfigureAll<JwtBearerOptions>(options =>
        {
            options.Events ??= new JwtBearerEvents();
            var failed = options.Events.OnAuthenticationFailed;
            options.Events.OnAuthenticationFailed = async context =>
            {
                refusals.Enqueue(context.Exception);
                await failed(context);
            };
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/me", (HttpContext context) => Results.Text($"{context.User.Identity!.Name}|{context.GetSupabaseCaller()}")).RequireAuthorization();
        app.MapGet("/who", (HttpContext context) => Results.Text(context.GetSupabaseCaller().ToString()));

        await app.StartAsync(cancellation);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new BearerApplication(app, new HttpClient { BaseAddress = new Uri(address) }, refusals, cancellation);
    }

    /// <summary>Asks the route that needs a user, with <paramref name="token"/> as its bearer token.</summary>
    public Task<Answer> AskAsync(string token) => SendAsync("/me", token);

    /// <summary>Asks the route that answers anybody who the request's caller is, with <paramref name="token"/> as its bearer token.</summary>
    public async Task<string> CallerOfAsync(string token) => (await SendAsync("/who", token)).Body;

    private async Task<Answer> SendAsync(string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _client.SendAsync(request, _cancellation);
        return new Answer(response.StatusCode, await response.Content.ReadAsStringAsync(_cancellation), response.Headers.WwwAuthenticate.ToString());
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    /// <summary>What the application answered.</summary>
    /// <param name="Status">The status.</param>
    /// <param name="Body">The body: for a user, their name and the request's caller.</param>
    /// <param name="Challenge">What a refused request is told about its token.</param>
    internal sealed record Answer(HttpStatusCode Status, string Body, string Challenge);
}
