namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// DDD00001 (value objects must be records), DDD00005 (must be partial) and DDD00013 (cannot be sealed).
/// <para>
/// Every test here asserts two things: the diagnostic fires at the type's identifier, <em>and</em> nothing
/// was generated for the type. The second half is the point — before these diagnostics existed a
/// misapplied attribute produced no code, no error and no clue, which is the failure mode
/// HANDOFF.md section 2.3 describes.
/// </para>
/// </summary>
public class ValueObjectDiagnosticTests
{
    private const string Usings = "using DDDToolkit.Abstractions.Attributes;\n\nnamespace Sample;\n\n";

    [Fact]
    public void ValueObject_on_a_class_reports_DDD00001_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [ValueObject]
            public partial class Money
            {
                public decimal Amount { get; protected init; }
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00001", at: "Money");
        result.ShouldNotHaveGeneratedFor("Money");
    }

    [Fact]
    public void ValueObject_on_a_struct_reports_DDD00001_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [ValueObject]
            public partial struct Money
            {
                public decimal Amount { get; init; }
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00001", at: "Money");
        result.ShouldNotHaveGeneratedFor("Money");
    }

    [Fact]
    public void ValueObject_on_a_record_struct_reports_DDD00001_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [ValueObject]
            public readonly partial record struct Money(decimal Amount);
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00001", at: "Money");
        result.ShouldNotHaveGeneratedFor("Money");
    }

    [Fact]
    public void SingleValueObject_on_a_class_reports_DDD00001_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [SingleValueObject<string>]
            public partial class Sku
            {
            }
            """).RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00001", at: "Sku");
        diagnostic.GetMessage().Should().Contain("SingleValueObject", "the message names the attribute that was misapplied");
        result.ShouldNotHaveGeneratedFor("Sku");
    }

    [Fact]
    public void SingleValueObject_on_a_record_struct_reports_DDD00001_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [SingleValueObject<string>]
            public readonly partial record struct Sku;
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00001", at: "Sku");
        result.ShouldNotHaveGeneratedFor("Sku");
    }

    [Fact]
    public void A_non_partial_value_object_reports_DDD00005_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [ValueObject]
            public record Money
            {
                public decimal Amount { get; protected init; }
            }
            """).RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00005", at: "Money");
        diagnostic.GetMessage().Should().Contain("partial");
        result.ShouldNotHaveDiagnostic("DDD00001");
        result.ShouldNotHaveGeneratedFor("Money");
    }

    [Fact]
    public void A_non_partial_single_value_object_reports_DDD00005_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [SingleValueObject<string>]
            public record Sku;
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00005", at: "Sku");
        result.ShouldNotHaveGeneratedFor("Sku");
    }

    [Fact]
    public void A_sealed_value_object_reports_DDD00013_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [ValueObject]
            public sealed partial record Money
            {
                public decimal Amount { get; protected init; }
            }
            """).RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00013", at: "Money");
        diagnostic.GetMessage().Should().Contain("ValidMoney", "the twin is what cannot be generated");
        result.ShouldNotHaveGeneratedFor("Money");
    }

    [Fact]
    public void A_sealed_single_value_object_reports_DDD00013_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [SingleValueObject<string>]
            public sealed partial record Sku;
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00013", at: "Sku");
        result.ShouldNotHaveGeneratedFor("Sku");
    }

    [Fact]
    public void A_sealed_non_partial_class_value_object_reports_all_three()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [ValueObject]
            public sealed class Money
            {
            }
            """).RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00001", "DDD00005", "DDD00013");
        result.ShouldNotHaveGeneratedFor("Money");
    }

    [Fact]
    public void A_well_formed_value_object_reports_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [ValueObject]
            public partial record Money
            {
                public decimal Amount { get; protected init; }
            }
            """).RunCore();

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.ShouldCompile();
    }
}
