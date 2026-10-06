using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
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
        failure.Which.Message.Should().Contain("could not be composed", "the composer's refusal ends the wait, before the five seconds do");
    }

    [Fact]
    public async Task Source_schemas_that_never_compose_end_the_wait_after_the_timeout_and_say_how_long_it_was()
    {
        // A source schema whose building never ends: nothing is composed and the composer refuses nothing, so
        // only the timeout ends the wait, and what it says is how long it waited and where to look. Asked through
        // the printed schema, which waits for the composed schema as the start does.
        using var building = new CancellationTokenSource();
        var app = Infrastructure.GatewayHost.Build(
            builder => NeverBuilt(builder, building.Token),
            configure: options => options.CompositionTimeout = TimeSpan.FromSeconds(1));
        try
        {
            var waiting = async () => await app.Services.GetRequiredService<InMemoryFusionSchemas>().PrintGatewayAsync(Cancellation);

            var failure = await waiting.Should().ThrowAsync<InvalidOperationException>();
            failure.Which.Message.Should().Contain("were not composed into one within 1 seconds").And.Contain("@shareable");
            failure.Which.InnerException.Should().BeNull("the composer said nothing");
        }
        finally
        {
            await building.CancelAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_wait_that_is_cancelled_is_a_cancellation_and_not_a_story_about_composition()
    {
        // The default timeout of thirty seconds, and a caller that stops waiting long before it.
        using var building = new CancellationTokenSource();
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var app = Infrastructure.GatewayHost.Build(builder => NeverBuilt(builder, building.Token));
        try
        {
            var waiting = app.Services.GetRequiredService<InMemoryFusionSchemas>().PrintGatewayAsync(stopping.Token);
            stopping.CancelAfter(TimeSpan.FromMilliseconds(250));

            await waiting.Invoking(async task => await task).Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            await building.CancelAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_composition_the_composer_refuses_fails_the_start_at_once_with_every_error()
    {
        // The default timeout of thirty seconds: the start does not wait for it when the composer said no. What
        // it says tells the two apart: a refusal "could not be composed", a wait that ran out "within" its seconds.
        var starting = StartAsync(builder =>
        {
            // Neither root query type is called Query, so there are two errors, and the composer's own
            // exception names the first one only.
            builder.Services.AddGraphQLServer("catalog").AddSourceSchemaDefaults().AddQueryType<CatalogQuery>();
            builder.Services.AddGraphQLServer("inventory").AddSourceSchemaDefaults().AddQueryType<InventoryQuery>();
        });

        var failure = await starting.Invoking(async task => await task).Should().ThrowAsync<InvalidOperationException>();

        failure.Which.Message.Should().Contain("could not be composed").And.Contain("2 errors").And.NotContain("within");
        failure.Which.Message.Should().Contain("The root query type in schema 'catalog' must be named 'Query'.");
        failure.Which.Message.Should().Contain("The root query type in schema 'inventory' must be named 'Query'.");
        failure.Which.InnerException.Should().NotBeNull("the composer's own exception, with its log, is kept");
    }

    [Fact]
    public async Task Source_schemas_that_are_built_already_still_fail_the_start_with_the_composers_error()
    {
        // The composer asks for the source schemas from its constructor, and tells a refusal only to who is
        // listening at that moment. Schemas that are built already are answered within that call: handed over
        // then, they would be composed and refused before anybody could listen, and the start would wait out
        // the thirty seconds and know no reason.
        await using var app = Infrastructure.GatewayHost.Build(builder =>
        {
            builder.Services.AddGraphQLServer("catalog").AddSourceSchemaDefaults().AddQueryType<CatalogQuery>();
            builder.Services.AddGraphQLServer("inventory").AddSourceSchemaDefaults().AddQueryType<InventoryQuery>(q => q.Name("Query"));
        });

        var sourceSchemas = app.Services.GetRequiredService<IRequestExecutorProvider>();
        foreach (var name in sourceSchemas.SchemaNames)
        {
            await sourceSchemas.GetExecutorAsync(name, Cancellation);
        }

        var starting = async () => await app.StartAsync(Cancellation);

        var failure = await starting.Should().ThrowAsync<InvalidOperationException>();

        failure.Which.Message.Should().Contain("could not be composed")
            .And.Contain("The root query type in schema 'catalog' must be named 'Query'.")
            .And.NotContain("within", "it is the composer's refusal, and not the wait running out");
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

    [Fact]
    public async Task The_gateway_without_a_name_composes_every_schema_of_the_application()
    {
        await using var shop = await Infrastructure.GatewayHost.StartAsync(builder =>
        {
            builder.Services.AddGraphQLServer("catalog").AddSourceSchemaDefaults().AddQueryType<CatalogQuery>(q => q.Name("Query"));
            builder.Services.AddGraphQLServer("admin").AddQueryType<AdminQuery>(q => q.Name("Query"));
        });

        shop.Schemas.GatewayNames.Should().Equal(InMemoryFusionGateway.DefaultName);
        shop.Schemas.SourceSchemaNames.Should().Equal("admin", "catalog");
        (await shop.DataAsync("{ stockValue }")).GetProperty("stockValue").GetInt32().Should().Be(144, "every schema of the application is one of its source schemas");
    }

    [Fact]
    public async Task The_gateway_without_a_name_is_registered_once()
    {
        var services = new ServiceCollection().AddInMemoryFusionGateway();

        var twice = () => services.AddInMemoryFusionGateway();

        twice.Should().Throw<InvalidOperationException>().WithMessage("AddInMemoryFusionGateway() is called twice*give each a name*");
    }

    /// <summary>Two modules that would compose, one of which never finishes building its schema, until <paramref name="released"/> is cancelled.</summary>
    private static void NeverBuilt(WebApplicationBuilder builder, CancellationToken released)
    {
        builder.Services.AddGraphQLServer("catalog").AddSourceSchemaDefaults().AddQueryType<CatalogQuery>(q => q.Name("Query"))
            .ConfigureSchemaAsync(async (_, _) => await Task.Delay(Timeout.Infinite, released));
        builder.Services.AddGraphQLServer("inventory").AddSourceSchemaDefaults().AddQueryType<InventoryQuery>(q => q.Name("Query"));
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

    /// <summary>What an administration's schema offers that no client of the shop's gateway is: the value of the stock.</summary>
    public sealed class AdminQuery
    {
        public int GetStockValue() => 144;
    }

    [EntityKey("id")]
    [GraphQLName("Product")]
    public sealed record InventoryProduct(int Id, int OnHand);
}
