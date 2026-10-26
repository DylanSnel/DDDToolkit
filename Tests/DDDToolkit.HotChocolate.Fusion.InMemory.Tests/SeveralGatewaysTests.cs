using DDDToolkit.HotChocolate.Fusion.InMemory.Tests.Infrastructure;
using FluentAssertions;
using Xunit;
using static DDDToolkit.HotChocolate.Fusion.InMemory.Tests.InMemoryFusionGatewayTests;

namespace DDDToolkit.HotChocolate.Fusion.InMemory.Tests;

/// <summary>
/// Several gateways in one application, each composing the schemas listed for it, each at an endpoint of its own:
/// a shop's at <c>/graphql</c> of Catalog and Inventory, and a back office's at <c>/admin/graphql</c> of Catalog and
/// an administration's schema the shop has nothing of.
/// </summary>
public sealed class SeveralGatewaysTests
{
    private const string Shop = "shop";
    private const string BackOffice = "back-office";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_gateways_compose_the_schemas_each_lists_and_answer_each_at_its_own_endpoint()
    {
        await using var app = await StartAsync();

        // The shop: Catalog's name and Inventory's stock, merged on the product's key.
        var product = (await app.DataAsync("/graphql", "{ productById(id: 1) { name onHand } }")).GetProperty("productById");
        product.GetProperty("name").GetString().Should().Be("Coffee");
        product.GetProperty("onHand").GetInt32().Should().Be(12);

        // The back office: Catalog as well, and the administration's field.
        var office = await app.DataAsync("/admin/graphql", "{ productById(id: 1) { name } stockValue }");
        office.GetProperty("productById").GetProperty("name").GetString().Should().Be("Coffee", "a schema may be composed by more than one gateway");
        office.GetProperty("stockValue").GetInt32().Should().Be(144);

        // What a gateway does not list is no field of it: refused when the document is read, before anything runs.
        (await app.PostAsync("/graphql", "{ stockValue }")).TryGetProperty("data", out _).Should().BeFalse("the shop composes no administration");
        (await app.PostAsync("/admin/graphql", "{ productById(id: 1) { onHand } }")).TryGetProperty("data", out _).Should().BeFalse("the back office composes no inventory");
    }

    [Fact]
    public async Task The_schemas_print_by_gateway_and_each_gateway_says_what_it_composes()
    {
        await using var app = await StartAsync();
        var schemas = app.Schemas;

        schemas.GatewayNames.Should().Equal(BackOffice, Shop);
        schemas.SourceSchemaNamesOf(Shop).Should().Equal("catalog", "inventory");
        schemas.SourceSchemaNamesOf(BackOffice).Should().Equal("admin", "catalog");
        schemas.SourceSchemaNames.Should().Equal(["admin", "catalog", "inventory"], "every schema some gateway composes, once");

        var shop = await schemas.PrintGatewayAsync(Shop, Cancellation);
        var office = await schemas.PrintGatewayAsync(BackOffice, Cancellation);
        shop.Should().Contain("onHand").And.NotContain("stockValue");
        office.Should().Contain("stockValue").And.NotContain("onHand");

        // With two gateways there is no "the" gateway to print.
        var which = async () => await schemas.PrintGatewayAsync(Cancellation);
        (await which.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*2 gateways, 'back-office' and 'shop': name the one to print*");

        var unknown = async () => await schemas.PrintGatewayAsync("Shop", Cancellation);
        (await unknown.Should().ThrowAsync<ArgumentException>()).WithMessage("*'Shop' is not a gateway*back-office, shop*");
    }

    [Fact]
    public async Task A_listed_schema_no_schema_is_registered_under_fails_the_mapping_and_says_which_there_are()
    {
        // "Admin" beside AddGraphQLServer("admin"): composed without it, the back office would lack its fields without a word.
        var app = GatewayHost.Build(
            Environments.Production,
            builder =>
            {
                Modules(builder);
                builder.Services.AddInMemoryFusionGateway(BackOffice, ["catalog", "Admin"]);
            },
            _ => { });
        await using (app)
        {
            var map = () => app.MapInMemoryFusionGateway("/admin/graphql", BackOffice);

            map.Should().Throw<InvalidOperationException>()
                .Which.Message.Should().Contain("The in-memory gateway 'back-office' composes 'Admin', which no schema of the application is registered under")
                .And.Contain("The schemas registered are 'admin', 'catalog', 'inventory'").And.Contain("in the same case");
        }
    }

    [Fact]
    public void A_second_gateway_of_one_name_is_refused_when_it_is_registered()
    {
        var services = new ServiceCollection().AddInMemoryFusionGateway(Shop, ["catalog"]);

        var again = () => services.AddInMemoryFusionGateway(Shop, ["inventory"]);

        again.Should().Throw<InvalidOperationException>().WithMessage("An in-memory gateway named 'shop' is registered already*");
    }

    [Fact]
    public void A_gateway_lists_at_least_one_schema()
    {
        var services = new ServiceCollection();

        var none = () => services.AddInMemoryFusionGateway(Shop, []);
        var empty = () => services.AddInMemoryFusionGateway(Shop, ["catalog", " "]);

        none.Should().Throw<ArgumentException>().WithMessage("The gateway 'shop' lists no source schema*");
        empty.Should().Throw<ArgumentException>().WithMessage("*an empty name*");
    }

    [Fact]
    public async Task Mapping_a_name_no_gateway_has_says_which_there_are()
    {
        var app = GatewayHost.Build(Environments.Production, Registered, _ => { });
        await using (app)
        {
            var named = () => app.MapInMemoryFusionGateway("/graphql", "Shop");
            var unnamed = () => app.MapInMemoryFusionGateway();

            named.Should().Throw<InvalidOperationException>().WithMessage("No in-memory gateway is registered as 'Shop'. The gateways registered are 'back-office', 'shop'*");
            unnamed.Should().Throw<InvalidOperationException>().WithMessage("MapInMemoryFusionGateway(path) serves the gateway AddInMemoryFusionGateway() registers*only named ones: 'back-office', 'shop'*");
        }
    }

    [Fact]
    public async Task A_gateway_that_is_registered_and_not_mapped_fails_the_start_and_says_which()
    {
        var starting = GatewayHost.StartAsync(Environments.Production, Registered, app => app.MapInMemoryFusionGateway("/graphql", Shop));

        var failure = await starting.Invoking(async task => await task).Should().ThrowAsync<InvalidOperationException>();
        failure.Which.Message.Should().Be("The in-memory gateway 'back-office' is registered and not mapped: call MapInMemoryFusionGateway(path, \"back-office\") on the application.");
    }

    [Fact]
    public async Task A_gateway_mapped_at_two_paths_is_one_gateway()
    {
        await using var app = await GatewayHost.StartAsync(
            Environments.Production,
            Registered,
            endpoints =>
            {
                endpoints.MapInMemoryFusionGateway("/graphql", Shop);
                endpoints.MapInMemoryFusionGateway("/shop/graphql", Shop);
                endpoints.MapInMemoryFusionGateway("/admin/graphql", BackOffice);
            });

        (await app.DataAsync("/graphql", "{ productById(id: 1) { onHand } }")).GetProperty("productById").GetProperty("onHand").GetInt32().Should().Be(12);
        (await app.DataAsync("/shop/graphql", "{ productById(id: 1) { onHand } }")).GetProperty("productById").GetProperty("onHand").GetInt32().Should().Be(12);
    }

    [Fact]
    public async Task Each_gateway_has_its_own_options()
    {
        // The back office bounds a request at two fields; the shop does not.
        await using var app = await GatewayHost.StartAsync(
            Environments.Production,
            builder =>
            {
                Modules(builder);
                builder.Services.AddInMemoryFusionGateway(Shop, ["catalog", "inventory"]);
                builder.Services.AddInMemoryFusionGateway(BackOffice, ["catalog", "admin"],
                    options => options.ConfigureGateway = gateway => gateway.ModifyParserOptions(parser => parser.MaxAllowedFields = 2));
            },
            Mapped);

        (await app.DataAsync("/graphql", "{ productById(id: 1) { id name onHand } }")).GetProperty("productById").GetProperty("onHand").GetInt32().Should().Be(12);
        (await app.PostAsync("/admin/graphql", "{ productById(id: 1) { id name } stockValue }")).TryGetProperty("data", out _).Should().BeFalse("four fields are more than the back office reads");
    }

    private static Task<GatewayHost> StartAsync() => GatewayHost.StartAsync(Environments.Production, Registered, Mapped);

    private static void Registered(WebApplicationBuilder builder)
    {
        Modules(builder);
        builder.Services.AddInMemoryFusionGateway(Shop, ["catalog", "inventory"]);
        builder.Services.AddInMemoryFusionGateway(BackOffice, ["catalog", "admin"]);
    }

    private static void Mapped(WebApplication app)
    {
        app.MapInMemoryFusionGateway("/graphql", Shop);
        app.MapInMemoryFusionGateway("/admin/graphql", BackOffice);
    }

    /// <summary>Catalog and Inventory, the shop's, and the administration's own source schema.</summary>
    private static void Modules(WebApplicationBuilder builder)
    {
        builder.Services.AddGraphQLServer("catalog").AddSourceSchemaDefaults().AddQueryType<CatalogQuery>(q => q.Name("Query"));
        builder.Services.AddGraphQLServer("inventory").AddSourceSchemaDefaults().AddQueryType<InventoryQuery>(q => q.Name("Query"));
        builder.Services.AddGraphQLServer("admin").AddSourceSchemaDefaults().AddQueryType<AdminQuery>(q => q.Name("Query"));
    }
}
