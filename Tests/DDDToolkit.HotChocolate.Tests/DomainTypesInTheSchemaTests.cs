using DDDToolkit.HotChocolate.Tests.Domain;
using DDDToolkit.HotChocolate.Tests.GraphQl;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.HotChocolate.Tests;

/// <summary>
/// What an entity, an aggregate or a value object publishes when nobody wrote its schema type: its
/// properties, never its methods. And in a Fusion source schema, a value object is <c>@shareable</c>.
/// </summary>
public class DomainTypesInTheSchemaTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_implicitly_bound_aggregate_publishes_its_properties_and_not_its_methods()
    {
        var basket = TypeBlock(await PrintAsync(), "type Basket");

        basket.Should().Contain("total: Price!");
        basket.Should().Contain("checkedOut: Int!");
        basket.Should().NotContain("checkout", "a method that changes state must not run inside a query");
    }

    [Fact]
    public async Task The_invariant_checks_of_an_entity_are_not_fields()
    {
        var basket = TypeBlock(await PrintAsync(), "type Basket");

        basket.Should().NotContain("invariantViolations");
        basket.Should().NotContain("ownInvariantViolations");
    }

    [Fact]
    public async Task An_implicitly_bound_value_object_publishes_its_data_and_not_its_behaviour()
    {
        var sdl = await PrintAsync();
        var price = TypeBlock(sdl, "type Price");

        price.Should().Contain("amount: Decimal!");
        price.Should().Contain("currency: String!");
        price.Should().Contain("display: String!", "a computed property is data");
        price.Should().NotContain("times");
        sdl.Should().NotContain("input PriceInput", "no field is left whose argument would need it");
    }

    [Fact]
    public async Task A_query_never_runs_a_method_of_the_aggregate()
    {
        var executor = await BuildAsync();

        var result = await executor.ExecuteAsync("{ basket { checkout } }", Cancellation);

        result.ToJson().Should().Contain("checkout").And.Contain("errors");
    }

    [Fact]
    public async Task A_type_that_binds_explicitly_publishes_the_methods_it_names()
    {
        var sdl = await PrintAsync(graphql => graphql.AddType<ExplicitBasketType>());

        TypeBlock(sdl, "type Basket").Should().Contain("checkout: Int!");
    }

    [Fact]
    public async Task A_type_extension_still_adds_its_fields_to_a_value_object()
    {
        var sdl = await PrintAsync(graphql => graphql.AddTypeExtension<PriceExtension>());

        TypeBlock(sdl, "type Price").Should().Contain("doubled: Decimal!");
    }

    [Fact]
    public async Task In_a_source_schema_a_value_object_is_shareable_and_an_entity_is_not()
    {
        var sdl = await PrintAsync(graphql => graphql.AddSourceSchemaDefaults());

        sdl.Should().Contain("type Price @shareable");
        sdl.Should().NotContain("type Basket @shareable");
    }

    [Fact]
    public async Task Outside_a_source_schema_nothing_is_marked_shareable()
    {
        var sdl = await PrintAsync();

        sdl.Should().NotContain("@shareable");
    }

    private static async Task<IRequestExecutor> BuildAsync(Action<IRequestExecutorBuilder>? configure = null)
    {
        var graphql = new ServiceCollection()
            .AddGraphQL()
            .AddDDDToolkitTypes()
            .AddGraphQlTestsGraphQlRuntimeBindings()
            .AddQueryType<BasketQuery>();

        configure?.Invoke(graphql);
        return await graphql.BuildRequestExecutorAsync(cancellationToken: Cancellation);
    }

    private static async Task<string> PrintAsync(Action<IRequestExecutorBuilder>? configure = null)
        => (await BuildAsync(configure)).Schema.ToString();

    /// <summary>The body of one SDL type declaration, so an assertion cannot match a different type.</summary>
    private static string TypeBlock(string sdl, string header)
    {
        var start = sdl.IndexOf(header + " ", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the schema should declare '{0}'", header);

        var open = sdl.IndexOf('{', start);
        var close = sdl.IndexOf('}', open);
        return sdl[open..close];
    }

    /// <summary>The query root: a basket, bound by convention.</summary>
    public sealed class BasketQuery
    {
        public Basket Basket() => new(BasketId.Create(Guid.CreateVersion7()), new Price(12.5m, "EUR"));
    }

    /// <summary>The basket, with its fields listed on purpose, a method among them.</summary>
    public sealed class ExplicitBasketType : ObjectType<Basket>
    {
        protected override void Configure(IObjectTypeDescriptor<Basket> descriptor)
        {
            descriptor.BindFieldsExplicitly();
            descriptor.Field(basket => basket.Id);
            descriptor.Field(basket => basket.Checkout());
        }
    }

    /// <summary>A field added to a value object from outside it.</summary>
    [ExtendObjectType<Price>]
    public sealed class PriceExtension
    {
        public decimal GetDoubled([Parent] Price price) => price.Amount * 2;
    }
}
