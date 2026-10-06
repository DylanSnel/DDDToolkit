using System.Net;
using DDDToolkit.HotChocolate.Fusion.InMemory.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Xunit;
using static DDDToolkit.HotChocolate.Fusion.InMemory.Tests.InMemoryFusionGatewayTests;

namespace DDDToolkit.HotChocolate.Fusion.InMemory.Tests;

/// <summary>
/// A gateway is an endpoint: what the host requires of its callers it puts on the gateway as on a route, with
/// <c>RequireAuthorization</c>, and each gateway requires its own.
/// </summary>
public sealed class GatewayAuthorizationTests
{
    private const string Product = "{ productById(id: 1) { name } }";

    [Fact]
    public async Task A_gateway_that_requires_authorization_challenges_a_request_without_a_token_and_answers_one_with()
    {
        await using var app = await StartAsync(endpoints =>
        {
            endpoints.MapInMemoryFusionGateway("/graphql", "user").RequireAuthorization();
            endpoints.MapInMemoryFusionGateway("/admin/graphql", "admin").RequireAuthorization(NamedCallers.Administrators);
        });

        using (var anonymous = await app.SendAsync("/graphql", Product))
        {
            anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await anonymous.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().BeEmpty("the endpoint's authorization challenged it, before the gateway read anything");
        }

        (await app.DataAsync("/graphql", Product, request: request => request.As("bob"))).GetProperty("productById").GetProperty("name").GetString().Should().Be("Coffee");
    }

    [Fact]
    public async Task Each_gateway_requires_what_its_own_endpoint_says()
    {
        await using var app = await StartAsync(endpoints =>
        {
            endpoints.MapInMemoryFusionGateway("/graphql", "user").RequireAuthorization();
            endpoints.MapInMemoryFusionGateway("/admin/graphql", "admin").RequireAuthorization(NamedCallers.Administrators);
        });

        // Bob is signed in, and no administrator: the user's gateway answers him, the administration's forbids him.
        (await app.DataAsync("/graphql", Product, request: request => request.As("bob"))).GetProperty("productById").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object);
        using (var forbidden = await app.SendAsync("/admin/graphql", "{ stockValue }", request: request => request.As("bob")))
        {
            forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        (await app.DataAsync("/admin/graphql", "{ stockValue }", request: request => request.As("ada"))).GetProperty("stockValue").GetInt32().Should().Be(144);
    }

    [Fact]
    public async Task A_gateway_mapped_in_a_group_takes_the_groups_requirements()
    {
        await using var app = await StartAsync(endpoints =>
        {
            var signedIn = endpoints.MapGroup("/api").RequireAuthorization();
            signedIn.MapInMemoryFusionGateway("/graphql", "user");
            signedIn.MapInMemoryFusionGateway("/admin/graphql", "admin");
        });

        using (var anonymous = await app.SendAsync("/api/graphql", Product))
        {
            anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        (await app.DataAsync("/api/graphql", Product, request: request => request.As("bob"))).GetProperty("productById").GetProperty("name").GetString().Should().Be("Coffee");
    }

    [Fact]
    public async Task The_applications_own_answer_to_a_refusal_stays_its_answer()
    {
        // A host that answers a refusal its own way, registered before the gateway: the gateway wraps it, and every
        // refusal that is no schema request is still answered by it.
        await using var app = await GatewayHost.StartAsync(
            Environments.Production,
            builder =>
            {
                builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, TeapotRefusals>();
                Registered(builder);
            },
            endpoints => Pipeline(endpoints, app =>
            {
                app.MapInMemoryFusionGateway("/graphql", "user").RequireAuthorization();
                app.MapInMemoryFusionGateway("/admin/graphql", "admin").RequireAuthorization();
            }));

        using var refused = await app.SendAsync("/graphql", Product);

        refused.StatusCode.Should().Be((HttpStatusCode)418, "the host's handler answers what the endpoint refused");
    }

    [Fact]
    public async Task An_answer_to_a_refusal_registered_after_the_gateway_fails_the_mapping_and_says_where_it_goes()
    {
        var app = GatewayHost.Build(
            Environments.Production,
            builder =>
            {
                Registered(builder);
                builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, TeapotRefusals>();
            },
            _ => { });
        await using (app)
        {
            var map = () => app.MapInMemoryFusionGateway("/graphql", "user");

            map.Should().Throw<InvalidOperationException>().WithMessage("An IAuthorizationMiddlewareResultHandler was registered after AddInMemoryFusionGateway()*Register it before*");
        }
    }

    [Fact]
    public async Task An_answer_to_a_refusal_the_host_makes_per_request_stays_per_request_and_the_key_still_passes()
    {
        // A host's answer that takes a scoped service of its own, as ASP.NET Core allows, since it asks for the answer
        // per request. The host is built as one under development is, so one resolved from the root would fail.
        const string Key = "q7Vb2LkP9xWm4RtZ8cYn3HsJ6dFg1AeU5oIr0TlKwQ8=";
        await using var app = await GatewayHost.StartAsync(
            Environments.Production,
            builder =>
            {
                builder.Configuration[GraphQLSchemaKey.Setting] = Key;
                builder.Services.AddScoped<RequestLanguage>();
                builder.Services.AddScoped<IAuthorizationMiddlewareResultHandler, LocalizedRefusals>();
                Registered(builder);
            },
            endpoints => Pipeline(endpoints, app =>
            {
                app.MapInMemoryFusionGateway("/graphql", "user").RequireAuthorization();
                app.MapInMemoryFusionGateway("/admin/graphql", "admin").RequireAuthorization();
            }));

        using (var refused = await app.SendAsync("/graphql", Product))
        {
            refused.StatusCode.Should().Be((HttpStatusCode)418);
            refused.Content.Headers.ContentLanguage.Should().ContainSingle().Which.Should().Be("nl", "the host's answer was made with the request's own services");
        }

        using var schema = new HttpRequestMessage(HttpMethod.Get, "/graphql?sdl");
        schema.Headers.Add(GraphQLSchemaKey.HeaderName, Key);
        using var read = await app.Http.SendAsync(schema, TestContext.Current.CancellationToken);
        read.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_one_gateway_that_requires_nothing_maps_beside_an_answer_to_a_refusal_registered_after_it()
    {
        // As in 3.1: nothing is required of the endpoint, so nothing about it is ever refused, and the order is no matter.
        await using var app = await GatewayHost.StartAsync(
            Environments.Production,
            builder =>
            {
                builder.Services.AddGraphQLServer("catalog").AddSourceSchemaDefaults().AddQueryType<CatalogQuery>(q => q.Name("Query"));
                builder.Services.AddInMemoryFusionGateway();
                builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, TeapotRefusals>();
            },
            endpoints => endpoints.MapInMemoryFusionGateway());

        (await app.DataAsync(Product)).GetProperty("productById").GetProperty("name").GetString().Should().Be("Coffee");
    }

    private static Task<GatewayHost> StartAsync(Action<WebApplication> gateways)
        => GatewayHost.StartAsync(Environments.Production, Registered, endpoints => Pipeline(endpoints, gateways));

    private static void Registered(WebApplicationBuilder builder)
    {
        builder.Services.AddNamedCallers();
        builder.Services.AddGraphQLServer("catalog").AddSourceSchemaDefaults().AddQueryType<CatalogQuery>(q => q.Name("Query"));
        builder.Services.AddGraphQLServer("admin").AddSourceSchemaDefaults().AddQueryType<AdminQuery>(q => q.Name("Query"));
        builder.Services.AddInMemoryFusionGateway("user", ["catalog"]);
        builder.Services.AddInMemoryFusionGateway("admin", ["admin"]);
    }

    private static void Pipeline(WebApplication app, Action<WebApplication> gateways)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        gateways(app);
    }

    /// <summary>A service that lives as long as a request: the language it is answered in.</summary>
    private sealed class RequestLanguage
    {
        public string Name => "nl";
    }

    /// <summary>A host's own answer to a refusal, made per request with a service of the request's: 418, in its language.</summary>
    private sealed class LocalizedRefusals(RequestLanguage language) : IAuthorizationMiddlewareResultHandler
    {
        private readonly AuthorizationMiddlewareResultHandler _otherwise = new();

        public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
        {
            if (authorizeResult.Succeeded)
            {
                return _otherwise.HandleAsync(next, context, policy, authorizeResult);
            }

            context.Response.StatusCode = 418;
            context.Response.Headers.ContentLanguage = language.Name;
            return Task.CompletedTask;
        }
    }

    /// <summary>A host's own answer to a refusal: 418, whatever was refused.</summary>
    private sealed class TeapotRefusals : IAuthorizationMiddlewareResultHandler
    {
        private readonly AuthorizationMiddlewareResultHandler _otherwise = new();

        public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
        {
            if (authorizeResult.Succeeded)
            {
                return _otherwise.HandleAsync(next, context, policy, authorizeResult);
            }

            context.Response.StatusCode = 418;
            return Task.CompletedTask;
        }
    }
}
