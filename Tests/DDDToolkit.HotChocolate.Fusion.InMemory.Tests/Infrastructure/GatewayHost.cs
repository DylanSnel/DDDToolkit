using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Xunit;

namespace DDDToolkit.HotChocolate.Fusion.InMemory.Tests.Infrastructure;

/// <summary>
/// An application with modules and the in-memory gateway, listening on a port of its own: what a test asks is
/// asked over real HTTP, through the pipeline, the gateway and the source schemas, as a client would.
/// </summary>
internal sealed class GatewayHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private GatewayHost(WebApplication app, HttpClient http)
    {
        _app = app;
        Http = http;
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>A client of the application.</summary>
    public HttpClient Http { get; }

    /// <summary>The application's own services, where its source schemas are.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>The application's schemas, printed.</summary>
    public InMemoryFusionSchemas Schemas => _app.Services.GetRequiredService<InMemoryFusionSchemas>();

    /// <summary>Builds and starts an application.</summary>
    /// <param name="modules">Registers each module's source schema, and whatever the modules need.</param>
    /// <param name="pipeline">Middleware that runs before the gateway, as a host's own would.</param>
    /// <param name="configure">Options for the gateway.</param>
    public static async Task<GatewayHost> StartAsync(
        Action<WebApplicationBuilder> modules,
        Action<WebApplication>? pipeline = null,
        Action<InMemoryFusionGatewayOptions>? configure = null)
    {
        var app = Build(modules, pipeline, configure);

        try
        {
            await app.StartAsync(Cancellation);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new GatewayHost(app, new HttpClient { BaseAddress = new Uri(address) });
    }

    /// <summary>Builds an application and maps the gateway, without starting it.</summary>
    public static WebApplication Build(
        Action<WebApplicationBuilder> modules,
        Action<WebApplication>? pipeline = null,
        Action<InMemoryFusionGatewayOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        // What a host under development runs with: a scoped service resolved from the root fails loudly.
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });

        modules(builder);
        builder.Services.AddInMemoryFusionGateway(configure);

        var app = builder.Build();
        pipeline?.Invoke(app);
        app.MapInMemoryFusionGateway();
        return app;
    }

    /// <summary>Posts a GraphQL request and returns the whole response body, errors and all.</summary>
    public async Task<JsonElement> PostAsync(string query, object? variables = null, Action<HttpRequestMessage>? request = null)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/graphql") { Content = JsonContent.Create(new { query, variables }) };
        request?.Invoke(message);

        using var response = await Http.SendAsync(message, Cancellation);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
    }

    /// <summary>Posts a GraphQL request and returns its <c>data</c>, failing the test when the response carries errors.</summary>
    public async Task<JsonElement> DataAsync(string query, object? variables = null, Action<HttpRequestMessage>? request = null)
    {
        var body = await PostAsync(query, variables, request);
        body.TryGetProperty("errors", out _).Should().BeFalse("the gateway answered {0}", body.ToString());
        return body.GetProperty("data");
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await _app.StopAsync(Cancellation);
        await _app.DisposeAsync();
    }
}
