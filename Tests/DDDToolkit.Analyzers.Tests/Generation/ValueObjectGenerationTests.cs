using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// What <c>[ValueObject]</c> and <c>[SingleValueObject&lt;T&gt;]</c> produce: the base type, the equality
/// components, the always-valid twin, and — the part worth testing hardest — which properties take part
/// in equality. <c>[DontCompare]</c> leaves a property out of equality; <c>[Internal]</c> leaves it out of
/// everything.
/// </summary>
public class ValueObjectGenerationTests
{
    private const string Preamble = "using DDDToolkit.Abstractions.Attributes;\nusing System;\n\nnamespace Sample;\n\n";

    private const string PersonName =
        """
        [ValueObject]
        public partial record PersonName
        {
            public PersonName(string firstName, string? middleNames, string lastName)
            {
                FirstName = firstName;
                MiddleNames = middleNames;
                LastName = lastName;
            }

            public string FirstName { get; protected init; }

            [DontCompare]
            public string? MiddleNames { get; protected init; }

            public string LastName { get; protected init; }

            [Internal]
            public int TimesRendered { get; protected set; }

            [DontCompare]
            public string FullName => string.Join(" ", FirstName, MiddleNames, LastName).Trim();
        }
        """;

    private static GeneratorRunOutcome Names() => GeneratorTestHost.Create(Preamble + PersonName).RunCore();

    // ------------------------------------------------------------------ [ValueObject]

    [Fact]
    public void A_value_object_derives_from_ValueObject()
    {
        var emitted = Names().Emit();

        emitted.Type("Sample.PersonName").BaseType.Should().Be(typeof(ValueObject));
    }

    [Fact]
    public void Equality_covers_every_property_except_DontCompare_and_Internal_ones_in_declaration_order()
    {
        var emitted = Names().Emit();
        var name = emitted.New("Sample.PersonName", "Ada", "King", "Lovelace");

        var components = ((IEnumerable<object?>)emitted.Call(name, "GetEqualityComponents")!).ToList();

        components.Should().Equal("Ada", "Lovelace");
    }

    [Fact]
    public void Two_value_objects_differing_only_in_a_DontCompare_property_are_equal()
    {
        var emitted = Names().Emit();

        var withMiddle = emitted.New("Sample.PersonName", "Ada", "King", "Lovelace");
        var withoutMiddle = emitted.New("Sample.PersonName", "Ada", null, "Lovelace");

        withMiddle.Should().Be(withoutMiddle);
        withMiddle.GetHashCode().Should().Be(withoutMiddle.GetHashCode());
    }

    [Fact]
    public void Two_value_objects_differing_in_a_compared_property_are_not_equal()
    {
        var emitted = Names().Emit();

        var ada = emitted.New("Sample.PersonName", "Ada", null, "Lovelace");
        var grace = emitted.New("Sample.PersonName", "Grace", null, "Hopper");

        ada.Should().NotBe(grace);
    }

    [Fact]
    public void An_Internal_property_is_left_out_of_equality_and_out_of_the_twin()
    {
        var result = Names();

        result.ShouldNotContain("Sample.PersonName.g.cs", "TimesRendered");

        var emitted = result.Emit();
        var name = emitted.New("Sample.PersonName", "Ada", null, "Lovelace");
        emitted.Type("Sample.PersonName").GetProperty("TimesRendered")!.SetValue(name, 7);

        var valid = emitted.Call(name, "ToValid")!;

        emitted.Property(valid, "TimesRendered").Should().Be(0, "an [Internal] property is auxiliary state, not part of the value");
        emitted.Property(valid, "FirstName").Should().Be("Ada");
        emitted.Property(valid, "LastName").Should().Be("Lovelace");
    }

    [Fact]
    public void The_twin_copies_the_compared_and_uncompared_properties_alike()
    {
        var emitted = Names().Emit();
        var name = emitted.New("Sample.PersonName", "Ada", "King", "Lovelace");

        var valid = emitted.Call(name, "ToValid")!;

        emitted.Property(valid, "MiddleNames").Should().Be("King", "[DontCompare] is about equality, not about storage");
    }

    [Fact]
    public void The_twin_derives_from_the_value_object_and_is_marked_IAlwaysValid()
    {
        var emitted = Names().Emit();

        var twin = emitted.Type("Sample.ValidPersonName");

        twin.BaseType.Should().Be(emitted.Type("Sample.PersonName"));
        typeof(IAlwaysValid).IsAssignableFrom(twin).Should().BeTrue();
    }

    [Fact]
    public void The_twin_refuses_an_invalid_value_object()
    {
        var emitted = GeneratorTestHost.Create(Preamble +
            """
            [ValueObject]
            public partial record Age
            {
                public Age(int years) => Years = years;

                public int Years { get; protected init; }

                protected override bool Validate() => Years >= 0;
            }
            """).RunCore().Emit();

        var invalid = emitted.New("Sample.Age", -1);

        var act = () => emitted.Call(invalid, "ToValid");

        act.Should().Throw<System.Reflection.TargetInvocationException>()
            .WithInnerException<DDDToolkit.Exceptions.InvalidValueObjectException>();
        emitted.Call(emitted.New("Sample.Age", 1), "ToValid").Should().NotBeNull();
    }

    [Fact]
    public void A_value_object_with_no_comparable_properties_still_compiles()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [ValueObject]
            public partial record Nothing;
            """).RunCore();

        result.ShouldCompile();
        result.ShouldContain("Sample.Nothing.g.cs", "yield break;");

        var emitted = result.Emit();
        var left = emitted.New("Sample.Nothing");
        ((IEnumerable<object?>)emitted.Call(left, "GetEqualityComponents")!).Should().BeEmpty();
        left.Should().Be(emitted.New("Sample.Nothing"));
    }

    [Fact]
    public void The_parameterless_constructor_is_protected_and_marked_for_System_Text_Json()
    {
        var result = Names();

        result.ShouldContain("Sample.PersonName.g.cs", "[global::System.Text.Json.Serialization.JsonConstructor]");

        var constructor = result.Emit().Type("Sample.PersonName")
            .GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, Type.EmptyTypes);

        constructor.Should().NotBeNull();
        constructor!.IsFamily.Should().BeTrue();
    }

    // ------------------------------------------------------------------ [SingleValueObject<T>]

    private static GeneratorRunOutcome EmailAddress()
        => GeneratorTestHost.Create(Preamble +
            """
            [SingleValueObject<string>(ColumnLength: 255)]
            public partial record EmailAddress
            {
                public static EmailAddress Create(string value) => new(value);
            }
            """).RunCore();

    [Fact]
    public void A_single_value_object_derives_from_SingleValueObject()
    {
        var result = EmailAddress();
        result.ShouldContain("Sample.EmailAddress.g.cs", "partial record EmailAddress : global::DDDToolkit.BaseTypes.SingleValueObject<string>");

        var emitted = result.Emit();
        emitted.Type("Sample.EmailAddress").BaseType.Should().Be(typeof(SingleValueObject<string>));
    }

    [Fact]
    public void A_single_value_object_compares_by_its_value()
    {
        var emitted = EmailAddress().Emit();

        var left = emitted.CallStatic("Sample.EmailAddress", "Create", "ada@example.com")!;
        var right = emitted.CallStatic("Sample.EmailAddress", "Create", "ada@example.com")!;

        emitted.Property(left, "Value").Should().Be("ada@example.com");
        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
        emitted.CallStatic("Sample.EmailAddress", "Create", "grace@example.com").Should().NotBe(left);
    }

    [Fact]
    public void A_single_value_object_gets_an_always_valid_twin()
    {
        var emitted = EmailAddress().Emit();
        var address = emitted.CallStatic("Sample.EmailAddress", "Create", "ada@example.com")!;

        var valid = emitted.Call(address, "ToValid")!;

        valid.Should().BeOfType(emitted.Type("Sample.ValidEmailAddress"));
        emitted.Property(valid, "Value").Should().Be("ada@example.com");
        typeof(IAlwaysValid).IsAssignableFrom(emitted.Type("Sample.ValidEmailAddress")).Should().BeTrue();
        emitted.New("Sample.ValidEmailAddress", "grace@example.com").Should().NotBeNull("the twin can also be built from the raw value");
    }

    [Fact]
    public void The_single_value_object_constructor_is_protected()
    {
        var emitted = EmailAddress().Emit();

        var constructor = emitted.Type("Sample.EmailAddress")
            .GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                [typeof(string)]);

        constructor.Should().NotBeNull();
        constructor!.IsFamily.Should().BeTrue("only the type itself decides how one is created");
    }
}
