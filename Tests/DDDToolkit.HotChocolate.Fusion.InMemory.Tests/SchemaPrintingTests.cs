using DDDToolkit.HotChocolate.Fusion.InMemory.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace DDDToolkit.HotChocolate.Fusion.InMemory.Tests;

/// <summary>
/// <see cref="InMemoryFusionSchemas"/>: the gateway's schema and each module's source schema as text, for a
/// test of an application to compare with committed files.
/// </summary>
public sealed class SchemaPrintingTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_gateways_schema_prints_as_one_schema_over_the_modules()
    {
        await using var shop = await GatewayHost.StartAsync(builder => builder.AddShop());

        shop.Schemas.SourceSchemaNames.Should().Equal(ShopModules.Catalog, ShopModules.Inventory);

        var sdl = await shop.Schemas.PrintGatewayAsync(Cancellation);

        // One Product, with what each module knows of it.
        Occurrences(sdl, "type Product ").Should().Be(1);
        var product = Block(sdl, "type Product");
        product.Should().Contain("name: String!", "Catalog answers the name");
        product.Should().Contain("onHand: Int!", "Inventory answers the stock");

        // Both modules' fields on the one Query and the one Mutation.
        Block(sdl, "type Query").Should().Contain("productById(id: Int!): Product").And.Contain("stockTotal: Int!");
        Block(sdl, "type Mutation").Should().Contain("productRename(").And.Contain("productRestock(");

        // What a client is offered, and nothing of how it is composed.
        sdl.Should().NotContain("@lookup").And.NotContain("@key").And.NotContain("@shareable").And.NotContain("@fusion__");
    }

    [Fact]
    public async Task A_source_schema_prints_by_its_name()
    {
        await using var shop = await GatewayHost.StartAsync(builder => builder.AddShop());

        var catalog = await shop.Schemas.PrintSourceAsync(ShopModules.Catalog, Cancellation);
        var inventory = await shop.Schemas.PrintSourceAsync(ShopModules.Inventory, Cancellation);

        // Each module's own part of the product, with the directives the gateway composes by.
        catalog.Should().Contain("type Product @key(fields: \"id\")");
        Block(catalog, "type Product").Should().Contain("name: String!").And.NotContain("onHand");
        catalog.Should().Contain("productById(id: Int!): Product @lookup");

        inventory.Should().Contain("type Product @key(fields: \"id\")");
        Block(inventory, "type Product").Should().Contain("onHand: Int!").And.NotContain("name:");
        inventory.Should().Contain("productById(id: Int!): Product @lookup @internal");

        // And what both declare says that it is shared.
        catalog.Should().Contain("type RefusalError implements CodedError @shareable");
        inventory.Should().Contain("type RefusalError implements CodedError @shareable");
    }

    [Fact]
    public async Task A_source_schema_prints_before_the_gateway_is_mapped_and_the_gateway_does_not()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddShop();
        builder.Services.AddInMemoryFusionGateway();
        await using var app = builder.Build();

        var schemas = app.Services.GetRequiredService<InMemoryFusionSchemas>();

        // A module's schema is the application's own, there from the moment its container is.
        schemas.SourceSchemaNames.Should().Equal(ShopModules.Catalog, ShopModules.Inventory);
        (await schemas.PrintSourceAsync(ShopModules.Catalog, Cancellation)).Should().Contain("type Product");

        // The composed one exists once the gateway is mapped.
        var print = async () => await schemas.PrintGatewayAsync(Cancellation);
        (await print.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*MapInMemoryFusionGateway*");
    }

    [Fact]
    public async Task An_unknown_name_is_refused()
    {
        await using var shop = await GatewayHost.StartAsync(builder => builder.AddShop());

        var unknown = async () => await shop.Schemas.PrintSourceAsync("payments", Cancellation);
        (await unknown.Should().ThrowAsync<ArgumentException>())
            .WithMessage("*'payments' is not a source schema*catalog, inventory*")
            .WithParameterName("name");

        // Names are matched as they were registered.
        var shouted = async () => await shop.Schemas.PrintSourceAsync("CATALOG", Cancellation);
        await shouted.Should().ThrowAsync<ArgumentException>();

        var empty = async () => await shop.Schemas.PrintSourceAsync("", Cancellation);
        await empty.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Source_schemas_that_do_not_compose_are_an_error_that_says_so_and_not_a_wait()
    {
        // An application that was built and mapped, and not started: nothing waited for the composed schema
        // yet, so printing it is what finds out that there is none.
        await using var app = GatewayHost.Build(
            builder =>
            {
                // Composition requires every root query type to be called Query; this one is not.
                builder.Services.AddGraphQLServer("catalog").AddSourceSchemaDefaults().AddQueryType<InMemoryFusionGatewayTests.CatalogQuery>();
                builder.Services.AddGraphQLServer("inventory").AddSourceSchemaDefaults().AddQueryType<InMemoryFusionGatewayTests.InventoryQuery>(q => q.Name("Query"));
            },
            configure: options => options.CompositionTimeout = TimeSpan.FromSeconds(5));

        var schemas = app.Services.GetRequiredService<InMemoryFusionSchemas>();

        var print = async () => await schemas.PrintGatewayAsync(Cancellation);
        (await print.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*could not be composed*'catalog'*'Query'*");

        // The module that is the problem still prints, which is where to look.
        (await schemas.PrintSourceAsync("catalog", Cancellation)).Should().Contain("query: CatalogQuery");
    }

    [Fact]
    public async Task The_printed_gateway_schema_is_what_the_endpoint_serves()
    {
        await using var shop = await GatewayHost.StartAsync(builder => builder.AddShop());

        var printed = await shop.Schemas.PrintGatewayAsync(Cancellation);
        var served = await shop.Http.GetStringAsync("/graphql?sdl", Cancellation);

        Normalized(printed).Should().Be(Normalized(served));
        printed.Should().Contain("type Product");
    }

    private static string Normalized(string sdl) => sdl.ReplaceLineEndings("\n").Trim();

    private static int Occurrences(string text, string value)
        => text.Split(value, StringSplitOptions.None).Length - 1;

    /// <summary>The body of one type declaration, so an assertion cannot match a different type.</summary>
    private static string Block(string sdl, string header)
    {
        var start = sdl.IndexOf(header + " ", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the schema should declare '{0}'", header);

        var open = sdl.IndexOf('{', start);
        var close = sdl.IndexOf('}', open);
        return sdl[open..close];
    }
}
