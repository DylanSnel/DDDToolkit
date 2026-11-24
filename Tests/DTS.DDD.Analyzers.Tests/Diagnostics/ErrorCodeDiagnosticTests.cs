namespace DTS.DDD.Analyzers.Tests.Diagnostics;

/// <summary>
/// The diagnostics of <c>[ErrorCode]</c>: DDD00005 for a record that is not partial, DDD00112 for a type that is no
/// record class deriving from <c>Error</c>, DDD00113 for a placeholder without a property and DDD00114 for two errors
/// with one code.
/// </summary>
public class ErrorCodeDiagnosticTests
{
    private const string Preamble = "using DTS.DDD.Abstractions.Attributes;\nusing DTS.DDD.Results;\n\nnamespace Shop;\n\n";

    private static GeneratorRunOutcome Run(string source) => GeneratorTestHost.Create(Preamble + source).RunCore();

    [Fact]
    public void A_record_that_is_not_partial_is_reported()
    {
        var result = Run(
            """
            [ErrorCode("stock.unknown", "There is no stock with SKU {Sku}.")]
            public sealed record StockUnknown(string Sku) : NotFoundError;
            """);

        result.ShouldHaveDiagnostic("DDD00005", "StockUnknown");
        result.ShouldNotHaveGeneratedFor("StockUnknown");
    }

    [Theory]
    [InlineData("public sealed partial record StockUnknown(string Sku);")]
    [InlineData("public sealed partial class StockUnknown { public string Sku { get; } = \"\"; }")]
    public void A_type_that_is_no_error_record_is_reported(string declaration)
    {
        var result = Run("[ErrorCode(\"stock.unknown\", \"There is no stock.\")]\n" + declaration);

        result.ShouldHaveDiagnostic("DDD00112", "StockUnknown");
        result.ShouldNotHaveGeneratedFor("StockUnknown");
    }

    [Fact]
    public void A_placeholder_without_a_property_is_reported()
    {
        var result = Run(
            """
            [ErrorCode("stock.unknown", "There is no stock with SKU {Code}.")]
            public sealed partial record StockUnknown(string Sku) : NotFoundError;
            """);

        result.ShouldHaveDiagnostic("DDD00113", "StockUnknown").GetMessage().Should().Contain("'{Code}'");
        result.ShouldNotHaveGeneratedFor("StockUnknown");
    }

    [Fact]
    public void An_unclosed_placeholder_is_reported()
    {
        var result = Run(
            """
            [ErrorCode("stock.unknown", "There is no stock with SKU {Sku.")]
            public sealed partial record StockUnknown(string Sku) : NotFoundError;
            """);

        result.ShouldHaveDiagnostic("DDD00113", "StockUnknown");
    }

    [Fact]
    public void Two_errors_with_one_code_are_reported_once_at_the_second()
    {
        var result = Run(
            """
            [ErrorCode("stock.unknown", "There is no stock with SKU {Sku}.")]
            public sealed partial record StockUnknown(string Sku) : NotFoundError;

            [ErrorCode("stock.unknown", "There is no stock called {Name}.")]
            public sealed partial record StockMissing(string Name) : NotFoundError;
            """);

        result.ShouldHaveDiagnostic("DDD00114", "StockUnknown");
        result.Count("DDD00114").Should().Be(1);
    }
}
