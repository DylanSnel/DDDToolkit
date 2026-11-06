using System.Globalization;
using DDDToolkit.Analyzers.Tests.Generation;

namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// DDD00077 to DDD00083: what a request says it requires with attributes, held to saying one thing, in one way, where
/// the generator can write it, with its markers in their place and on the ids they mark. Each is an error, because a
/// request that reads as if it said what it requires and says something else, or nothing, is a door a review would
/// take for closed. A request that says nothing at all stays an error too.
/// </summary>
public partial class RequirementAttributeDiagnosticTests
{

    private static string Message(GeneratorRunOutcome result, string id, string at)
        => result.ShouldHaveDiagnostic(id, at).GetMessage(CultureInfo.InvariantCulture);

    [Fact]
    public void Where_the_interface_is_not_partial_the_compiler_asks_for_the_property_itself()
    {
        var result = GeneratorTestHost.Create(RequirementAttributeGenerationTests.Billing.Replace("public partial interface", "public interface", StringComparison.Ordinal))
            .WithSource("namespace Shop.Billing;\n\npublic sealed record CloseInvoice(int Number) : IBillingRequest;", "Requests.cs")
            .WithDependencyInjection()
            .RunCore();

        result.ShouldHaveExactlyDiagnostics();
        result.CompilationErrors.Select(error => error.Id).Should().Equal(["CS0535"], "a forgotten requirement is an error either way, the compiler's here");
    }

    [Fact]
    public void A_request_in_parts_that_says_nothing_is_DDD00077_whichever_part_implements_the_interface()
    {
        // The first part is bare, which is not where the request is read: the second, with the interface, is.
        var result = GeneratorTestHost.Create(RequirementAttributeGenerationTests.Billing, "Billing.cs")
            .WithSource("namespace Shop.Billing;\n\npublic partial record SplitForgotten;", "First.cs")
            .WithSource("namespace Shop.Billing;\n\npublic partial record SplitForgotten : IBillingRequest;", "Second.cs")
            .WithDependencyInjection()
            .RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00077");
        Message(result, "DDD00077", at: "SplitForgotten").Should().Contain("'SplitForgotten' implements 'IBillingRequest' and says nothing of what it requires");
    }

    // ------------------------------------------------------------------ DDD00083: where they are read

    [Fact]
    public void Attributes_on_a_request_whose_interface_is_not_partial_are_DDD00083_with_the_line_to_write()
    {
        var result = GeneratorTestHost.Create(RequirementAttributeGenerationTests.Billing.Replace("public partial interface", "public interface", StringComparison.Ordinal))
            .WithSource(
                """
                using DDDToolkit.Access;

                namespace Shop.Billing;

                [SignedIn]
                public sealed record MyInvoices : IBillingRequest
                {
                    AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.SignedIn();
                }
                """,
                "Requests.cs")
            .WithDependencyInjection()
            .RunCore();

        result.ShouldHaveDiagnostic("DDD00083", at: "MyInvoices").GetMessage(CultureInfo.InvariantCulture).Should()
            .Contain("'IBillingRequest' is not partial").And.Contain("declare it 'public partial interface IBillingRequest : IRequireAccess;'");
    }
}
