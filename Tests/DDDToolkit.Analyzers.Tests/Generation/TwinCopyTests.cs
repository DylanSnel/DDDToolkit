using System.Reflection;
using DDDToolkit.Exceptions;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// The always-valid twin has to hold exactly the value that was validated. It is built from the
/// record's copy constructor, which copies every field. It used to start from an empty object and copy
/// the settable properties one by one, which left a get-only property or a private field at its default,
/// so <c>ToValid()</c> handed back a different value from the one it had checked, and made a
/// <c>protected</c> property fail to compile (CS1540: it cannot be read through a <c>Money</c>).
/// </summary>
public class TwinCopyTests
{
    private const string Preamble = "using DDDToolkit.Abstractions.Attributes;\n\nnamespace Sample;\n\n";

    private static GeneratorRunOutcome Run(string source) => GeneratorTestHost.Create(Preamble + source).RunCore();

    [Fact]
    public void A_get_only_property_arrives_in_the_twin()
    {
        var result = Run(
            """
            [ValueObject]
            public partial record Money
            {
                public Money(decimal amount) => Amount = amount;

                public decimal Amount { get; }
            }
            """);

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.ShouldCompile();

        var emitted = result.Emit();
        var money = emitted.New("Sample.Money", 42m);
        var valid = emitted.Call(money, "ToValid")!;

        emitted.Property(valid, "Amount").Should().Be(42m);
    }

    [Fact]
    public void A_protected_property_compiles_and_arrives_in_the_twin()
    {
        var result = Run(
            """
            [ValueObject]
            public partial record Money
            {
                public Money(decimal amount) => Amount = amount;

                protected decimal Amount { get; init; }
            }
            """);

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.ShouldCompile();

        var emitted = result.Emit();
        var valid = emitted.Call(emitted.New("Sample.Money", 42m), "ToValid")!;

        emitted.Property(valid, "Amount").Should().Be(42m);
    }

    [Fact]
    public void A_private_field_arrives_in_the_twin()
    {
        var result = Run(
            """
            [ValueObject]
            public partial record Money
            {
                private readonly string _source;

                public Money(decimal amount, string source) => (Amount, _source) = (amount, source);

                public decimal Amount { get; protected init; }

                public string Source() => _source;
            }
            """);

        result.ShouldCompile();

        var emitted = result.Emit();
        var valid = emitted.Call(emitted.New("Sample.Money", 1m, "import"), "ToValid")!;

        emitted.Call(valid, "Source").Should().Be("import");
    }

    [Fact]
    public void An_invalid_value_still_throws_before_a_twin_exists()
    {
        var result = Run(
            """
            [ValueObject]
            public partial record Money
            {
                public Money(decimal amount) => Amount = amount;

                public decimal Amount { get; }

                protected override bool Validate() => Amount >= 0;
            }
            """);

        var emitted = result.Emit();
        var act = () => emitted.Call(emitted.New("Sample.Money", -1m), "ToValid");

        act.Should().Throw<TargetInvocationException>().WithInnerException<InvalidValueObjectException>();
    }

    [Fact]
    public void A_single_value_object_keeps_its_other_state_in_the_twin()
    {
        var result = Run(
            """
            [SingleValueObject<string>]
            public partial record EmailAddress
            {
                public static EmailAddress Create(string value) => new(value) { Source = "signup" };

                public string? Source { get; private init; }
            }
            """);

        result.ShouldCompile();

        var emitted = result.Emit();
        var email = emitted.CallStatic("Sample.EmailAddress", "Create", "ada@example.com")!;
        var valid = emitted.Call(email, "ToValid")!;

        emitted.Property(valid, "Value").Should().Be("ada@example.com");
        emitted.Property(valid, "Source").Should().Be("signup");
    }

    [Fact]
    public void A_record_identifier_keeps_its_value_and_prefix_in_the_twin()
    {
        var result = Run(
            """
            [EntityId<Guid>("ORD")]
            public partial record OrderId;
            """.Replace("Guid", "System.Guid"));

        result.ShouldCompile();

        var emitted = result.Emit();
        var id = emitted.CallStatic("Sample.OrderId", "CreateUnique")!;
        var valid = emitted.Call(id, "ToValid")!;

        emitted.Property(valid, "Value").Should().Be(emitted.Property(id, "Value"));
        valid.ToString().Should().Be(id.ToString());
    }
}
