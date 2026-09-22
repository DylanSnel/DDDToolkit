namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// DDD00010 (value object properties need a protected setter) and DDD00011 (they need an init setter).
/// The two are independent checks over the same setter, so the combinations matter:
/// <list type="table">
/// <item><term><c>public set</c></term><description>both fire</description></item>
/// <item><term><c>public init</c></term><description>only DDD00010 (not protected)</description></item>
/// <item><term><c>protected set</c></term><description>only DDD00011 (not init)</description></item>
/// <item><term><c>protected init</c></term><description>neither</description></item>
/// <item><term>no setter</term><description>neither — nothing can mutate it</description></item>
/// </list>
/// </summary>
public class ValueObjectPropertyDiagnosticTests
{
    private const string Usings = "using DDDToolkit.Abstractions.Attributes;\n\nnamespace Sample;\n\n";

    private static GeneratorRunOutcome WithProperty(string declaration)
        => GeneratorTestHost.Create(Usings +
            $$"""
            [ValueObject]
            public partial record Money
            {
                {{declaration}}
            }
            """).RunCore();

    [Fact]
    public void A_public_set_reports_both_DDD00010_and_DDD00011()
    {
        var result = WithProperty("public decimal Amount { get; set; }");

        result.ShouldHaveDiagnostic("DDD00010", at: "Amount");
        result.ShouldHaveDiagnostic("DDD00011", at: "Amount");
        result.ShouldHaveExactlyDiagnostics("DDD00010", "DDD00011");
    }

    [Fact]
    public void A_public_init_reports_only_DDD00010()
    {
        var result = WithProperty("public decimal Amount { get; init; }");

        result.ShouldHaveDiagnostic("DDD00010", at: "Amount");
        result.ShouldHaveExactlyDiagnostics("DDD00010");
    }

    [Fact]
    public void A_protected_set_reports_only_DDD00011()
    {
        var result = WithProperty("public decimal Amount { get; protected set; }");

        result.ShouldHaveDiagnostic("DDD00011", at: "Amount");
        result.ShouldHaveExactlyDiagnostics("DDD00011");
    }

    [Fact]
    public void A_protected_init_reports_nothing()
    {
        var result = WithProperty("public decimal Amount { get; protected init; }");

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.ShouldCompile();
    }

    [Fact]
    public void A_get_only_property_reports_nothing()
    {
        var result = WithProperty("public decimal Amount { get; } = 1m;");

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.ShouldCompile();
    }

    [Fact]
    public void A_computed_property_reports_nothing()
    {
        var result = WithProperty("public decimal Amount => 1m;");

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.ShouldCompile();
    }

    [Fact]
    public void An_internal_property_is_exempt_from_both()
    {
        // [Internal] means "auxiliary, not part of the stored/compared state", so its setter is not
        // policed - and it is left out of the generated equality and of the twin's copy constructor.
        var result = WithProperty("[Internal] public decimal Scratch { get; set; }");

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.ShouldNotContain(Hint.Of("Sample.Money"), "Scratch");
        result.ShouldCompile();
    }

    [Fact]
    public void Every_offending_property_is_reported_separately()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [ValueObject]
            public partial record Money
            {
                public decimal Amount { get; set; }

                public string Currency { get; init; } = "EUR";

                public decimal Rate { get; protected init; }
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00011", at: "Amount");
        result.ShouldHaveDiagnostic("DDD00010", at: "Amount");
        result.ShouldHaveDiagnostic("DDD00010", at: "Currency");
        result.ShouldHaveExactlyDiagnostics("DDD00010", "DDD00010", "DDD00011");
    }

    [Fact]
    public void A_value_object_with_a_bad_setter_is_still_generated_because_the_error_already_fails_the_build()
    {
        // DDD00010/DDD00011 are errors, so nothing reaches a running program either way. Suppressing
        // generation as well would bury the real complaint under a pile of "type has no base class"
        // errors, so the generator emits its half and lets the diagnostic speak.
        var result = WithProperty("public decimal Amount { get; set; }");

        result.ShouldHaveGenerated(Hint.Of("Sample.Money"));
        result.ShouldContain(Hint.Of("Sample.Money"), "yield return Amount;");
        result.CompilationErrors.Should().BeEmpty("the generated code itself is valid; only the DDD diagnostics fail the build");
    }
}
