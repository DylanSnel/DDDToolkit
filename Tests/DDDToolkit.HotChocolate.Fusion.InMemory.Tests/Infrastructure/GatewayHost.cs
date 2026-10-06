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

    /// <summary>Builds and starts an application with the one gateway, of every schema, at <c>/graphql</c>.</summary>
    /// <param name="modules">Registers each module's source schema, and whatever the modules need.</param>
    /// <param name="pipeline">Middleware that runs before the gateway, as a host's own would.</param>
    /// <param name="configure">Options for the gateway.</param>
    public static Task<GatewayHost> StartAsync(
        Action<WebApplicationBuilder> modules,
        Action<WebApplication>? pipeline = null,
        Action<InMemoryFusionGatewayOptions>? configure = null)
        => StartAsync(Build(modules, pipeline, configure));

    /// <summary>
    /// Builds and starts an application in <paramref name="environment"/>, whose gateways and endpoints the test
    /// registers and maps itself.
    /// </summary>
    /// <param name="environment">The environment it runs in: what a schema request needs depends on it.</param>
    /// <param name="services">Registers the source schemas, the gateways and whatever else the application has.</param>
    /// <param name="endpoints">Its pipeline and its endpoints, the gateways' among them.</param>
    public static Task<GatewayHost> StartAsync(string environment, Action<WebApplicationBuilder> services, Action<WebApplication> endpoints)
        => StartAsync(Build(environment, services, endpoints));

    /// <summary>Builds an application and maps the one gateway, without starting it.</summary>
    public static WebApplication Build(
        Action<WebApplicationBuilder> modules,
        Action<WebApplication>? pipeline = null,
        Action<InMemoryFusionGatewayOptions>? configure = null)
        => Build(
            Environments.Production,
            builder =>
            {
                modules(builder);
                builder.Services.AddInMemoryFusionGateway(configure);
            },
            app =>
            {
                pipeline?.Invoke(app);
                app.MapInMemoryFusionGateway();
            });

    /// <summary>Builds an application in <paramref name="environment"/> as the test describes it, without starting it.</summary>
    public static WebApplication Build(string environment, Action<WebApplicationBuilder> services, Action<WebApplication> endpoints)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        // What a host under development runs with: a scoped service resolved from the root fails loudly.
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });

        services(builder);

        var app = builder.Build();
        endpoints(app);
        return app;
    }

    private static async Task<GatewayHost> StartAsync(WebApplication app)
    {
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

    /// <summary>Posts a GraphQL request and returns the whole response body, errors and all.</summary>
    public async Task<JsonElement> PostAsync(string query, object? variables = null, Action<HttpRequestMessage>? request = null)
        => await PostAsync("/graphql", query, variables, request);

    /// <summary>Posts a GraphQL request to the gateway at <paramref name="path"/> and returns the whole response body.</summary>
    public async Task<JsonElement> PostAsync(string path, string query, object? variables = null, Action<HttpRequestMessage>? request = null)
    {
        using var response = await SendAsync(path, query, variables, request);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
    }

    /// <summary>Posts a GraphQL request to the gateway at <paramref name="path"/> and returns the response, for its status.</summary>
    public async Task<HttpResponseMessage> SendAsync(string path, string query, object? variables = null, Action<HttpRequestMessage>? request = null)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(new { query, variables }) };
        request?.Invoke(message);
        return await Http.SendAsync(message, Cancellation);
    }

    /// <summary>Posts a GraphQL request and returns its <c>data</c>, failing the test when the response carries errors.</summary>
    public async Task<JsonElement> DataAsync(string query, object? variables = null, Action<HttpRequestMessage>? request = null)
        => await DataAsync("/graphql", query, variables, request);

    /// <summary>Posts a GraphQL request to the gateway at <paramref name="path"/> and returns its <c>data</c>, failing the test on errors.</summary>
    public async Task<JsonElement> DataAsync(string path, string query, object? variables = null, Action<HttpRequestMessage>? request = null)
    {
        var body = await PostAsync(path, query, variables, request);
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
