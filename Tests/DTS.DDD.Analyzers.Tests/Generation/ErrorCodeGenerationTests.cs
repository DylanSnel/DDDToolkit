using DTS.DDD.Results;

namespace DTS.DDD.Analyzers.Tests.Generation;

/// <summary>
/// An error record with <c>[ErrorCode]</c> gets <c>Code</c>, <c>Message</c> and <c>Arguments</c> from the generator.
/// <c>Message</c> fills the placeholders from the properties with the invariant culture, and <c>Arguments</c> holds the
/// public properties of the record and of its bases below <c>Error</c>.
/// </summary>
public class ErrorCodeGenerationTests
{
    private const string Preamble = "using DTS.DDD.Abstractions.Attributes;\nusing DTS.DDD.Results;\n\nnamespace Shop;\n\n";

    private static GeneratorRunOutcome Run(string source) => GeneratorTestHost.Create(Preamble + source).RunCore();

    [Fact]
    public void A_record_gets_its_code_message_and_arguments()
    {
        var result = Run(
            """
            [ErrorCode("stock.out-of-stock", "Only {Available} left of {Sku}.")]
            public sealed partial record OutOfStock(string Sku, int Available) : ConflictError;
            """);

        result.ShouldCompile();
        var error = (Error)result.Emit().New("Shop.OutOfStock", "tea", 5);

        error.Should().BeAssignableTo<ConflictError>();
        error.Code.Should().Be("stock.out-of-stock");
        error.Message.Should().Be("Only 5 left of tea.");
        error.Arguments.Should().BeEquivalentTo(new Dictionary<string, object?> { ["Sku"] = "tea", ["Available"] = 5 });
        error.Arguments["available"].Should().Be(5, "the arguments of an error are matched without regard to case");
    }

    [Fact]
    public void The_message_uses_the_invariant_culture_and_a_format_of_the_placeholder()
    {
        var result = Run(
            """
            [ErrorCode("price.too-low", "The price {Price:0.00} is below {Minimum}.")]
            public sealed partial record PriceTooLow(decimal Price, decimal Minimum) : InvalidError;
            """);

        result.ShouldCompile();
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("nl-NL");
            var error = (Error)result.Emit().New("Shop.PriceTooLow", 1.5m, 2.25m);

            error.Message.Should().Be("The price 1.50 is below 2.25.");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Doubled_braces_are_literal_braces()
    {
        var result = Run(
            """
            [ErrorCode("template.broken", "The template {{name}} misses {Name}.")]
            public sealed partial record TemplateBroken(string Name) : InvalidError;
            """);

        result.ShouldCompile();
        var error = (Error)result.Emit().New("Shop.TemplateBroken", "title");

        error.Message.Should().Be("The template {name} misses title.");
    }

    [Fact]
    public void A_record_without_properties_has_no_arguments()
    {
        var result = Run(
            """
            [ErrorCode("basket.empty", "The basket is empty.")]
            public sealed partial record BasketEmpty() : ConflictError;
            """);

        result.ShouldCompile();
        var error = (Error)result.Emit().New("Shop.BasketEmpty");

        error.Message.Should().Be("The basket is empty.");
        error.Arguments.Should().BeEmpty();
    }

    [Fact]
    public void Two_errors_with_the_same_values_are_equal()
    {
        var result = Run(
            """
            [ErrorCode("stock.unknown", "There is no stock with SKU {Sku}.")]
            public sealed partial record StockUnknown(string Sku) : NotFoundError;
            """);

        result.ShouldCompile();
        var emitted = result.Emit();

        emitted.New("Shop.StockUnknown", "tea").Should().Be(emitted.New("Shop.StockUnknown", "tea"));
        emitted.New("Shop.StockUnknown", "tea").Should().NotBe(emitted.New("Shop.StockUnknown", "coffee"));
    }

    [Fact]
    public void A_property_of_a_base_record_is_an_argument_and_a_placeholder()
    {
        var result = Run(
            """
            public abstract record StockError(string Sku) : ConflictError;

            [ErrorCode("stock.blocked", "Stock {Sku} is blocked until {Until}.")]
            public sealed partial record StockBlocked(string Sku, string Until) : StockError(Sku);
            """);

        result.ShouldCompile();
        var error = (Error)result.Emit().New("Shop.StockBlocked", "tea", "Monday");

        error.Message.Should().Be("Stock tea is blocked until Monday.");
        error.Arguments.Keys.Should().BeEquivalentTo("Sku", "Until");
    }
}
