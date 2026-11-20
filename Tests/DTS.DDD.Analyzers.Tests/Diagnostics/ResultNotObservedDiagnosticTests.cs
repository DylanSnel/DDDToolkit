namespace DTS.DDD.Analyzers.Tests.Diagnostics;

/// <summary>
/// DDD00111: a call that returns a <c>Result</c> or <c>Result&lt;T&gt;</c>, directly or awaited, stands as a statement
/// of its own, so its failure is lost. A result that is read, returned, passed on or discarded with <c>_ =</c> is not
/// reported.
/// </summary>
public class ResultNotObservedDiagnosticTests
{
    private const string Stock =
        """
        using System.Threading.Tasks;
        using DTS.DDD.Results;

        namespace Shop;

        public static class Stock
        {
            public static Result Reserve() => Result.Success();

            public static Result<int> Count() => 3;

            public static ValueTask<Result<int>> CountAsync() => ValueTask.FromResult(Result<int>.Success(3));
        }
        """;

    private static GeneratorRunOutcome Run(string body)
        => GeneratorTestHost.Create(Stock, "Stock.cs")
            .WithAnalyzers(GeneratorTestHost.CoreAnalyzers())
            .WithSource(
                $$"""
                using System.Threading.Tasks;
                using DTS.DDD.Results;

                namespace Shop;

                public static class Caller
                {
                    public static async Task<int> Run()
                    {
                        {{body}}
                    }
                }
                """,
                "Caller.cs")
            .RunCore();

    [Theory]
    [InlineData("Stock.Reserve(); return 0;", "Reserve")]
    [InlineData("Stock.Count(); return 0;", "Count")]
    [InlineData("await Stock.CountAsync(); return 0;", "CountAsync")]
    public void A_dropped_result_is_reported_at_the_call(string body, string method)
        => Run(body).ShouldHaveDiagnostic("DDD00111", at: "Stock." + method + "()");

    [Theory]
    [InlineData("return Stock.Count().Value;")]
    [InlineData("var count = await Stock.CountAsync(); return count.IsSuccess ? count.Value : 0;")]
    [InlineData("_ = Stock.Reserve(); return 0;")]
    [InlineData("Stock.Reserve().ThrowIfFailure(); return 0;")]
    [InlineData("Stock.Count().Tap(count => System.Console.WriteLine(count)); return 0;")]
    public void A_result_that_is_read_or_discarded_reports_nothing(string body)
        => Run(body).AnalyzerDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == "DDD00111");
}