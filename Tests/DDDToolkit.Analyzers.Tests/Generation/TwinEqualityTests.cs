namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// Equality between a value and its always-valid twin has to be symmetric. It used to hold one way
/// only: <c>plain == twin</c> compared the components and said yes, while <c>twin == plain</c> went
/// through the <c>Equals(Money?)</c> the compiler synthesizes in the derived record, which casts to
/// <c>ValidMoney</c> and said no. The compiler does not allow that member to be declared, so a twin
/// cannot be made equal to a plain value from its own side. The generated equality therefore follows
/// record semantics: values of different runtime types are never equal, and two twins, or two plain
/// values, with the same components are.
/// </summary>
public class TwinEqualityTests
{
    private const string Preamble = "using DDDToolkit.Abstractions.Attributes;\n\nnamespace Sample;\n\n";

    /// <summary>Both operators and the typed Equals, compiled against the base type as ordinary code would be.</summary>
    private const string Compare =
        """

        public static class Compare
        {
            public static bool Operator(T left, T right) => left == right;

            public static bool NotOperator(T left, T right) => left != right;

            public static bool TypedEquals(T left, T right) => left.Equals(right);
        }
        """;

    private static EmittedAssembly Emit(string source, string typeName)
    {
        var result = GeneratorTestHost.Create(Preamble + source + Compare.Replace("T ", typeName + " ")).RunCore();

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.ShouldCompile();
        return result.Emit();
    }

    private static void ShouldBeUnequalBothWays(EmittedAssembly emitted, object plain, object twin)
    {
        foreach (var (left, right) in new[] { (plain, twin), (twin, plain) })
        {
            emitted.CallStatic("Sample.Compare", "Operator", left, right).Should().Be(false);
            emitted.CallStatic("Sample.Compare", "NotOperator", left, right).Should().Be(true);
            emitted.CallStatic("Sample.Compare", "TypedEquals", left, right).Should().Be(false);
            left.Equals(right).Should().BeFalse();
        }
    }

    private static void ShouldBeEqualBothWays(EmittedAssembly emitted, object first, object second)
    {
        foreach (var (left, right) in new[] { (first, second), (second, first) })
        {
            emitted.CallStatic("Sample.Compare", "Operator", left, right).Should().Be(true);
            emitted.CallStatic("Sample.Compare", "NotOperator", left, right).Should().Be(false);
            emitted.CallStatic("Sample.Compare", "TypedEquals", left, right).Should().Be(true);
            left.Equals(right).Should().BeTrue();
        }

        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    // ------------------------------------------------------------------ [ValueObject]

    private const string Money =
        """
        [ValueObject]
        public partial record Money(decimal Amount, string Currency);
        """;

    [Fact]
    public void A_value_object_and_its_twin_are_unequal_in_both_directions()
    {
        var emitted = Emit(Money, "Money");
        var plain = emitted.New("Sample.Money", 8m, "EUR");
        var twin = emitted.Call(emitted.New("Sample.Money", 8m, "EUR"), "ToValid")!;

        ShouldBeUnequalBothWays(emitted, plain, twin);
    }

    [Fact]
    public void Two_twins_of_the_same_value_object_are_equal()
    {
        var emitted = Emit(Money, "Money");
        var first = emitted.Call(emitted.New("Sample.Money", 8m, "EUR"), "ToValid")!;
        var second = emitted.Call(emitted.New("Sample.Money", 8m, "EUR"), "ToValid")!;

        ShouldBeEqualBothWays(emitted, first, second);
        ShouldBeUnequalBothWays(emitted, first, emitted.Call(emitted.New("Sample.Money", 9m, "EUR"), "ToValid")!);
    }

    [Fact]
    public void Two_plain_value_objects_are_still_equal()
    {
        var emitted = Emit(Money, "Money");

        ShouldBeEqualBothWays(emitted, emitted.New("Sample.Money", 8m, "EUR"), emitted.New("Sample.Money", 8m, "EUR"));
    }

    // ------------------------------------------------------------------ [SingleValueObject<T>]

    private const string EmailAddress =
        """
        [SingleValueObject<string>]
        public partial record EmailAddress;
        """;

    [Fact]
    public void A_single_value_object_and_its_twin_are_unequal_in_both_directions()
    {
        var emitted = Emit(EmailAddress, "EmailAddress");
        var plain = emitted.New("Sample.EmailAddress", "ada@example.com");
        var twin = emitted.Call(emitted.New("Sample.EmailAddress", "ada@example.com"), "ToValid")!;

        ShouldBeUnequalBothWays(emitted, plain, twin);
        ShouldBeEqualBothWays(emitted, twin, emitted.New("Sample.ValidEmailAddress", "ada@example.com"));
        ShouldBeEqualBothWays(emitted, plain, emitted.New("Sample.EmailAddress", "ada@example.com"));
    }

    // ------------------------------------------------------------------ [EntityId<T>] partial record

    private const string OrderId =
        """
        [EntityId<int>("ORD")]
        public partial record OrderId;
        """;

    [Fact]
    public void A_record_identifier_and_its_twin_are_unequal_in_both_directions()
    {
        var emitted = Emit(OrderId, "OrderId");
        var plain = emitted.New("Sample.OrderId", 7);
        var twin = emitted.Call(emitted.New("Sample.OrderId", 7), "ToValid")!;

        ShouldBeUnequalBothWays(emitted, plain, twin);
        ShouldBeEqualBothWays(emitted, twin, emitted.New("Sample.ValidOrderId", 7));
        ShouldBeEqualBothWays(emitted, plain, emitted.New("Sample.OrderId", 7));
    }
}
