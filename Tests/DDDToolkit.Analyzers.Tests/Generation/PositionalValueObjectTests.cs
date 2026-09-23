using System.Collections.Immutable;
using System.Reflection;
using DDDToolkit.Analyzers.Analyzers;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// A positional <c>[ValueObject]</c> record, and the <c>With(...)</c> every value object gets.
/// <para>
/// The compiler makes each positional parameter a <c>public init</c> property, which would let any caller
/// use <c>with</c> to copy the value into a state that never passed validation. The generator declares
/// the properties itself as <c>protected init</c>, which takes the place of the synthesized ones. The
/// generated <c>With(...)</c> stands in for <c>with</c> outside the type, and on the always-valid twin it
/// validates the copy before handing it back.
/// </para>
/// </summary>
public class PositionalValueObjectTests
{
    private const string Preamble = "using DDDToolkit.Abstractions.Attributes;\n\nnamespace Sample;\n\n";

    private const string Money =
        """
        [ValueObject]
        public partial record Money(decimal Amount, string Currency, [property: DontCompare] string? Note)
        {
            protected override bool Validate() => Amount >= 0;
        }

        public static class Use
        {
            public static Money Make() => new(10m, "EUR", "first");

            public static Money WithAmount(Money money, decimal amount) => money.With(amount: amount);

            public static Money ClearNote(Money money) => money.With(note: null);

            public static ValidMoney ValidWithAmount(ValidMoney money, decimal amount) => money.With(amount: amount);

            // Ordinary code that works with Money and is handed a ValidMoney.
            public static Money Discount(Money money) => money.With(amount: money.Amount - 100);

            public static string Split(Money money)
            {
                var (amount, currency, _) = money;
                return amount + " " + currency;
            }
        }
        """;

    private static GeneratorRunOutcome Run(string source = Money) => GeneratorTestHost.Create(Preamble + source).RunCore();

    // ------------------------------------------------------------------ the record

    [Fact]
    public void The_parameters_become_protected_init_properties_and_nothing_is_reported()
    {
        var result = Run();

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.ShouldCompile();
        result.ShouldContain(Hint.Of("Sample.Money"), "public decimal Amount { get; protected init; } = Amount;");
        result.ShouldContain(Hint.Of("Sample.Money"), "public string Currency { get; protected init; } = Currency;");
    }

    [Fact]
    public void A_with_expression_outside_the_type_does_not_compile()
    {
        var result = GeneratorTestHost.Create(Preamble + Money)
            .WithSource("namespace Sample; static class Outside { static object M(Money money) => money with { Amount = -1 }; }", "Outside.cs")
            .RunCore();

        result.CompilationErrors.Select(error => error.Id).Should().Equal("CS0272");
    }

    [Fact]
    public void The_primary_constructor_fills_the_properties_and_deconstruction_still_works()
    {
        var emitted = Run().Emit();
        var money = emitted.CallStatic("Sample.Use", "Make")!;

        emitted.Property(money, "Amount").Should().Be(10m);
        emitted.Property(money, "Currency").Should().Be("EUR");
        emitted.Property(money, "Note").Should().Be("first");
        emitted.CallStatic("Sample.Use", "Split", money).Should().Be("10 EUR");
    }

    [Fact]
    public void A_property_attribute_on_a_parameter_moves_to_the_generated_property()
    {
        var result = Run();
        result.ShouldContain(Hint.Of("Sample.Money"), "[global::DDDToolkit.Abstractions.Attributes.DontCompareAttribute]");

        var emitted = result.Emit();
        var first = emitted.New("Sample.Money", 10m, "EUR", "first");
        var second = emitted.New("Sample.Money", 10m, "EUR", "second");

        first.Should().Be(second, "Note is [property: DontCompare]");
    }

    [Fact]
    public async Task The_compiler_warning_about_the_ignored_property_target_is_suppressed()
    {
        // CS0657 says the attribute is ignored; the generator copied it, so the suppressor silences it.
        var warnings = await CompilerWarningsWithSuppressor(Run());

        warnings.Should().ContainSingle(warning => warning.Id == "CS0657")
            .Which.IsSuppressed.Should().BeTrue();
    }

    [Fact]
    public async Task The_warning_stays_when_a_property_declared_by_hand_really_does_lose_the_attribute()
    {
        var warnings = await CompilerWarningsWithSuppressor(Run(
            """
            [ValueObject]
            public partial record Money(decimal Amount, [property: DontCompare] string Note)
            {
                public string Note { get; protected init; } = Note;
            }
            """));

        warnings.Should().ContainSingle(warning => warning.Id == "CS0657")
            .Which.IsSuppressed.Should().BeFalse();
    }

    private static async Task<IReadOnlyList<Diagnostic>> CompilerWarningsWithSuppressor(GeneratorRunOutcome outcome)
    {
        var options = new CompilationWithAnalyzersOptions(
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            onAnalyzerException: null,
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false,
            reportSuppressedDiagnostics: true);

        var diagnostics = await outcome.OutputCompilation
            .WithAnalyzers([new PositionalAttributeSuppressor()], options)
            .GetAllDiagnosticsAsync();

        return [.. diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning)];
    }

    [Fact]
    public void The_twin_copies_every_positional_property()
    {
        var emitted = Run().Emit();
        var valid = emitted.Call(emitted.CallStatic("Sample.Use", "Make")!, "ToValid")!;

        valid.Should().BeAssignableTo<IAlwaysValid>();
        emitted.Property(valid, "Amount").Should().Be(10m);
        emitted.Property(valid, "Note").Should().Be("first");
    }

    [Theory]
    [InlineData("public partial record Percentage(decimal Value);")]
    [InlineData("public partial record Nothing();")]
    [InlineData("public partial record Money(decimal Amount) { public string Currency { get; protected init; } = \"EUR\"; }")]
    [InlineData("public partial record Money(decimal Amount, string Currency) { public string Currency { get; protected init; } = Currency.ToUpperInvariant(); }")]
    public void Other_positional_shapes_compile(string declaration)
    {
        // One parameter: the chained constructor must not be ambiguous with the copy constructor.
        // None: the primary constructor is the parameterless one, so no second one is generated.
        // A property declared by hand for a parameter replaces the synthesized one, and the generator
        // leaves it alone.
        var result = Run("[ValueObject]\n" + declaration);

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.ShouldCompile();
    }

    // ------------------------------------------------------------------ With(...)

    [Fact]
    public void With_replaces_what_is_passed_and_keeps_the_rest()
    {
        var emitted = Run().Emit();
        var copy = emitted.CallStatic("Sample.Use", "WithAmount", emitted.CallStatic("Sample.Use", "Make"), 25m)!;

        emitted.Property(copy, "Amount").Should().Be(25m);
        emitted.Property(copy, "Currency").Should().Be("EUR");
        emitted.Property(copy, "Note").Should().Be("first");
    }

    [Fact]
    public void With_can_set_a_nullable_property_to_null()
    {
        var emitted = Run().Emit();
        var copy = emitted.CallStatic("Sample.Use", "ClearNote", emitted.CallStatic("Sample.Use", "Make"))!;

        emitted.Property(copy, "Note").Should().BeNull();
        emitted.Property(copy, "Amount").Should().Be(10m);
    }

    [Fact]
    public void With_on_a_plain_value_object_hands_back_an_invalid_copy_for_the_caller_to_judge()
    {
        var emitted = Run().Emit();
        var copy = emitted.CallStatic("Sample.Use", "WithAmount", emitted.CallStatic("Sample.Use", "Make"), -1m)!;

        copy.GetType().Name.Should().Be("Money");
        emitted.Property(copy, "IsValid").Should().Be(false);
    }

    [Fact]
    public void With_on_the_twin_hands_back_a_twin()
    {
        var emitted = Run().Emit();
        var valid = emitted.Call(emitted.CallStatic("Sample.Use", "Make")!, "ToValid")!;

        var copy = emitted.CallStatic("Sample.Use", "ValidWithAmount", valid, 25m)!;

        copy.GetType().Name.Should().Be("ValidMoney");
        emitted.Property(copy, "Amount").Should().Be(25m);
    }

    [Fact]
    public void With_on_the_twin_throws_at_the_call_when_the_copy_is_invalid()
    {
        var emitted = Run().Emit();
        var valid = emitted.Call(emitted.CallStatic("Sample.Use", "Make")!, "ToValid")!;

        var act = () => emitted.CallStatic("Sample.Use", "ValidWithAmount", valid, -1m);

        act.Should().Throw<TargetInvocationException>().WithInnerException<InvalidValueObjectException>();
    }

    [Fact]
    public void With_on_a_twin_held_as_its_base_type_still_throws()
    {
        // The case a with-expression cannot catch: code written against Money, handed a ValidMoney,
        // would get back a ValidMoney that is not valid. With is virtual, so the twin's version runs.
        var emitted = Run().Emit();
        var valid = emitted.Call(emitted.CallStatic("Sample.Use", "Make")!, "ToValid")!;

        var act = () => emitted.CallStatic("Sample.Use", "Discount", valid);

        act.Should().Throw<TargetInvocationException>().WithInnerException<InvalidValueObjectException>();
    }

    [Fact]
    public void A_value_object_with_declared_properties_gets_With_too()
    {
        var result = Run(
            """
            [ValueObject]
            public partial record Address
            {
                public Address(string street, string city) => (Street, City) = (street, city);

                public string Street { get; protected init; }

                public string City { get; protected init; }
            }

            public static class Use
            {
                public static Address Move(Address address) => address.With(city: "Utrecht");
            }
            """);

        result.ShouldCompile();
        var emitted = result.Emit();
        var moved = emitted.CallStatic("Sample.Use", "Move", emitted.New("Sample.Address", "Main St", "Amsterdam"))!;

        emitted.Property(moved, "Street").Should().Be("Main St");
        emitted.Property(moved, "City").Should().Be("Utrecht");
    }

    [Fact]
    public void A_With_the_author_declared_is_left_alone()
    {
        var result = Run(
            """
            [ValueObject]
            public partial record Money(decimal Amount)
            {
                public Money With(decimal amount) => new(amount);
            }
            """);

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.ShouldCompile();
        result.ShouldNotContain(Hint.Of("Sample.Money"), "With(global::DDDToolkit.BaseTypes.Optional");
    }

    [Fact]
    public void With_is_marked_Internal_so_integrations_keep_it_out_of_their_schemas()
    {
        var emitted = Run().Emit();

        foreach (var typeName in new[] { "Sample.Money", "Sample.ValidMoney" })
        {
            var with = emitted.Type(typeName).GetMethods().Single(method => method.Name == "With" && method.DeclaringType!.FullName == typeName);
            with.GetCustomAttributes().Select(attribute => attribute.GetType().Name).Should().Contain("InternalAttribute");
        }
    }
}
