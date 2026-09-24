using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Types.Composite;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Xunit;

namespace DDDToolkit.HotChocolate.Fusion.InMemory.Tests;

/// <summary>
/// Two modules in one application, each with its own <c>Product</c>, served as one schema at /graphql by
/// the in-memory gateway, over real HTTP.
/// </summary>
public sealed class InMemoryFusionGatewayTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task One_query_is_answered_by_both_modules_merged_on_the_key()
    {
        await using var shop = await StartAsync(builder =>
        {
            builder.Services.AddGraphQLServer("catalog").AddSourceSchemaDefaults().AddQueryType<CatalogQuery>(q => q.Name("Query"));
            builder.Services.AddGraphQLServer("inventory").AddSourceSchemaDefaults().AddQueryType<InventoryQuery>(q => q.Name("Query"));
        });

        var data = await shop.QueryAsync("{ productById(id: 1) { id name onHand } }");

        var product = data.GetProperty("productById");
        product.GetProperty("name").GetString().Should().Be("Coffee", "Catalog answers the name");
        product.GetProperty("onHand").GetInt32().Should().Be(12, "Inventory answers the stock");
    }

    [Fact]
    public async Task A_source_schema_that_cannot_be_composed_fails_the_start_instead_of_hanging()
    {
        var starting = StartAsync(
            builder =>
            {
                // Composition requires every root query type to be called Query; this one is not.
                builder.Services.AddGraphQLServer("catalog").AddSourceSchemaDefaults().AddQueryType<CatalogQuery>();
                builder.Services.AddGraphQLServer("inventory").AddSourceSchemaDefaults().AddQueryType<InventoryQuery>(q => q.Name("Query"));
            },
            options => options.CompositionTimeout = TimeSpan.FromSeconds(5));

        var failure = await starting.Invoking(async task => await task).Should().ThrowAsync<InvalidOperationException>();
        failure.Which.Message.Should().Contain("composed");
    }

    [Fact]
    public async Task Mapping_the_gateway_without_a_source_schema_says_what_is_missing()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddInMemoryFusionGateway();
        await using var app = builder.Build();

        var map = () => app.MapInMemoryFusionGateway();

        map.Should().Throw<InvalidOperationException>().WithMessage("*No source schema*");
    }

    private static async Task<Shop> StartAsync(Action<WebApplicationBuilder> modules, Action<InMemoryFusionGatewayOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        modules(builder);
        builder.Services.AddInMemoryFusionGateway(configure);

        var app = builder.Build();
        app.MapInMemoryFusionGateway();

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
        return new Shop(app, new HttpClient { BaseAddress = new Uri(address) });
    }

    private sealed class Shop(WebApplication app, HttpClient http) : IAsyncDisposable
    {
        public async Task<JsonElement> QueryAsync(string query)
        {
            using var response = await http.PostAsJsonAsync("/graphql", new { query }, Cancellation);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
            body.TryGetProperty("errors", out var errors).Should().BeFalse("the gateway answered {0}", body.ToString());
            return body.GetProperty("data");
        }

        public async ValueTask DisposeAsync()
        {
            http.Dispose();
            await app.StopAsync(Cancellation);
            await app.DisposeAsync();
        }
    }

    public sealed class CatalogQuery
    {
        [Lookup]
        public CatalogProduct? GetProductById(int id) => new(id, "Coffee");
    }

    [EntityKey("id")]
    [GraphQLName("Product")]
    public sealed record CatalogProduct(int Id, string Name);

    public sealed class InventoryQuery
    {
        [Lookup]
        [Internal]
        public InventoryProduct? GetProductById(int id) => new(id, 12);
    }

    [EntityKey("id")]
    [GraphQLName("Product")]
    public sealed record InventoryProduct(int Id, int OnHand);
}
