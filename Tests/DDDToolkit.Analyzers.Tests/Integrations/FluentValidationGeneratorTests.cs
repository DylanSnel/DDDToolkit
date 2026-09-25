using FluentValidation.Results;

namespace DDDToolkit.Analyzers.Tests.Integrations;

/// <summary>
/// DDDToolkit.FluentValidation.Analyzers gives every value object, single value object and reference-type
/// entity id an <c>Errors</c> collection, a <c>Validate()</c> override that runs a nested
/// <c>Validator : AbstractValidator&lt;T&gt;</c>, and the empty half of that validator for the author to fill
/// in. Struct ids are valid by construction and get nothing.
/// </summary>
public class FluentValidationGeneratorTests
{
    private const string Preamble = "using DDDToolkit.Abstractions.Attributes;\nusing FluentValidation;\nusing System;\n\nnamespace Sample;\n\n";

    private static GeneratorRunOutcome Run(string source)
        => GeneratorTestHost.Create(Preamble + source)
            .WithFluentValidation()
            .RunCoreAnd(GeneratorTestHost.FluentValidationGenerators());

    [Fact]
    public void A_single_value_object_gets_Errors_Validate_and_a_validator_to_fill_in()
    {
        var result = Run(
            """
            [SingleValueObject<string>]
            public partial record EmailAddress
            {
                public static EmailAddress Create(string value) => new(value);

                partial class Validator
                {
                    public Validator() => RuleFor(x => x.Value).NotEmpty().EmailAddress();
                }
            }
            """);

        result.ShouldCompile();
        result.ShouldContain(Hint.Of("Sample.EmailAddress", ".FluentValidation"), "partial class Validator : global::FluentValidation.AbstractValidator<global::Sample.EmailAddress>");
        result.ShouldContain(Hint.Of("Sample.EmailAddress", ".FluentValidation"), "protected override bool Validate()");
        result.ShouldContain(Hint.Of("Sample.EmailAddress", ".FluentValidation"), "public global::System.Collections.ObjectModel.ReadOnlyCollection<global::FluentValidation.Results.ValidationFailure> Errors");
    }

    [Fact]
    public void The_authors_rules_decide_whether_the_value_object_is_valid()
    {
        var emitted = Run(
            """
            [SingleValueObject<string>]
            public partial record EmailAddress
            {
                public static EmailAddress Create(string value) => new(value);

                partial class Validator
                {
                    public Validator() => RuleFor(x => x.Value).NotEmpty().EmailAddress();
                }
            }
            """).Emit();

        var good = emitted.CallStatic("Sample.EmailAddress", "Create", "ada@example.com")!;
        var bad = emitted.CallStatic("Sample.EmailAddress", "Create", "not-an-address")!;

        emitted.Property(good, "IsValid").Should().Be(true);
        emitted.Property(bad, "IsValid").Should().Be(false);
        ((IEnumerable<ValidationFailure>)emitted.Property(good, "Errors")!).Should().BeEmpty();
        ((IEnumerable<ValidationFailure>)emitted.Property(bad, "Errors")!)
            .Should().ContainSingle().Which.PropertyName.Should().Be("Value");
    }

    [Fact]
    public void An_invalid_value_object_cannot_become_its_always_valid_twin()
    {
        var emitted = Run(
            """
            [SingleValueObject<string>]
            public partial record EmailAddress
            {
                public static EmailAddress Create(string value) => new(value);

                partial class Validator
                {
                    public Validator() => RuleFor(x => x.Value).EmailAddress();
                }
            }
            """).Emit();

        var bad = emitted.CallStatic("Sample.EmailAddress", "Create", "nope")!;

        var act = () => emitted.Call(bad, "ToValid");

        act.Should().Throw<System.Reflection.TargetInvocationException>()
            .WithInnerException<DDDToolkit.Exceptions.InvalidValueObjectException>();
    }

    [Fact]
    public void A_multi_property_value_object_gets_a_validator_over_the_whole_record()
    {
        var emitted = Run(
            """
            [ValueObject]
            public partial record PersonName
            {
                public PersonName(string firstName, string lastName)
                {
                    FirstName = firstName;
                    LastName = lastName;
                }

                public string FirstName { get; protected init; }

                public string LastName { get; protected init; }

                partial class Validator
                {
                    public Validator()
                    {
                        RuleFor(x => x.FirstName).NotEmpty();
                        RuleFor(x => x.LastName).NotEmpty();
                    }
                }
            }
            """).Emit();

        var complete = emitted.New("Sample.PersonName", "Ada", "Lovelace");
        var incomplete = emitted.New("Sample.PersonName", "", "");

        emitted.Property(complete, "IsValid").Should().Be(true);
        emitted.Property(incomplete, "IsValid").Should().Be(false);
        ((IEnumerable<ValidationFailure>)emitted.Property(incomplete, "Errors")!)
            .Select(failure => failure.PropertyName).Should().BeEquivalentTo(["FirstName", "LastName"]);
    }

    [Fact]
    public void A_record_entity_id_gets_a_validator_too()
    {
        var result = Run(
            """
            [EntityId<Guid>("USR")]
            public partial record UserId
            {
                public static UserId Create(Guid value) => new(value);

                partial class Validator
                {
                    public Validator() => RuleFor(x => x.Value).NotEqual(Guid.Empty);
                }
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveGenerated(Hint.Of("Sample.UserId", ".FluentValidation"));

        var emitted = result.Emit();
        emitted.Property(emitted.CallStatic("Sample.UserId", "Create", Guid.NewGuid())!, "IsValid").Should().Be(true);
        emitted.Property(emitted.CallStatic("Sample.UserId", "Create", Guid.Empty)!, "IsValid").Should().Be(false);
    }

    [Fact]
    public void A_struct_entity_id_gets_no_validator()
    {
        // A struct id has no base type to override Validate on, and nothing to be invalid about.
        var result = Run(
            """
            [EntityId<Guid>]
            public readonly partial record struct CatId;
            """);

        result.ShouldCompile();
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains("FluentValidation", StringComparison.Ordinal));
    }

    [Fact]
    public void An_id_generated_from_the_aggregate_gets_no_validator_either()
    {
        // It is a struct id like any other, which this generator passes over.
        var result = Run(
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }
            }
            """);

        result.ShouldCompile();
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains("FluentValidation", StringComparison.Ordinal));
    }

    [Fact]
    public void An_entity_gets_no_validator()
    {
        var result = Run(
            """
            [EntityId<Guid>]
            public readonly partial record struct BasketId;

            [AggregateRoot<BasketId>]
            public partial class Basket
            {
                public Basket(BasketId id) : base(id) { }
            }
            """);

        result.ShouldCompile();
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains("FluentValidation", StringComparison.Ordinal));
    }

    [Fact]
    public void A_value_object_without_rules_is_valid()
    {
        var emitted = Run(
            """
            [SingleValueObject<string>]
            public partial record Sku
            {
                public static Sku Create(string value) => new(value);
            }
            """).Emit();

        emitted.Property(emitted.CallStatic("Sample.Sku", "Create", "anything")!, "IsValid").Should().Be(true);
    }

    [Fact]
    public void A_value_object_that_writes_its_own_Validate_is_left_alone()
    {
        // The generated part would declare Validate() a second time (CS0111). The author's rule is the rule.
        var result = Run(
            """
            [SingleValueObject<string>]
            public partial record Sku
            {
                public static Sku Create(string value) => new(value);

                protected override bool Validate() => Value.Length == 8;
            }
            """);

        result.ShouldCompile();
        result.GeneratedSources.Should().NotContain(source => source.HintName == Hint.Of("Sample.Sku", ".FluentValidation"));

        var emitted = result.Emit();
        emitted.Property(emitted.CallStatic("Sample.Sku", "Create", "SKU-0001")!, "IsValid").Should().Be(true);
        emitted.Property(emitted.CallStatic("Sample.Sku", "Create", "SKU-1")!, "IsValid").Should().Be(false);
    }

    [Fact]
    public void A_value_object_that_gives_its_own_reasons_in_another_part_is_left_alone()
    {
        // Validate(ValidationErrorBuilder) counts too, whichever part of the type declares it.
        var result = Run(
            """
            [ValueObject]
            public partial record PersonName
            {
                public PersonName(string firstName, string lastName)
                {
                    FirstName = firstName;
                    LastName = lastName;
                }

                public string FirstName { get; protected init; }

                public string LastName { get; protected init; }
            }

            public partial record PersonName
            {
                protected override void Validate(DDDToolkit.Validation.ValidationErrorBuilder errors)
                {
                    if (FirstName.Length == 0)
                    {
                        errors.Add("A first name is required.", nameof(FirstName));
                    }
                }
            }
            """);

        result.ShouldCompile();
        result.GeneratedSources.Should().NotContain(source => source.HintName == Hint.Of("Sample.PersonName", ".FluentValidation"));

        var emitted = result.Emit();
        emitted.Property(emitted.New("Sample.PersonName", "Ada", "Lovelace"), "IsValid").Should().Be(true);
        emitted.Property(emitted.New("Sample.PersonName", "", "Lovelace"), "IsValid").Should().Be(false);
    }

    [Fact]
    public void One_project_can_mix_hand_validated_and_FluentValidation_value_objects()
    {
        var result = Run(
            """
            [SingleValueObject<string>]
            public partial record Sku
            {
                public static Sku Create(string value) => new(value);

                protected override bool Validate() => Value.Length == 8;
            }

            [SingleValueObject<string>]
            public partial record EmailAddress
            {
                public static EmailAddress Create(string value) => new(value);

                partial class Validator
                {
                    public Validator() => RuleFor(x => x.Value).EmailAddress();
                }
            }
            """);

        result.ShouldCompile();
        result.GeneratedSources.Should().NotContain(source => source.HintName == Hint.Of("Sample.Sku", ".FluentValidation"));
        result.ShouldContain(Hint.Of("Sample.EmailAddress", ".FluentValidation"), "partial class Validator : global::FluentValidation.AbstractValidator<global::Sample.EmailAddress>");

        var emitted = result.Emit();
        emitted.Property(emitted.CallStatic("Sample.EmailAddress", "Create", "nope")!, "IsValid").Should().Be(false);
        emitted.Property(emitted.CallStatic("Sample.Sku", "Create", "SKU-1")!, "IsValid").Should().Be(false);
    }

    [Fact]
    public void A_type_the_core_generator_refused_gets_no_validator_either()
    {
        var result = Run(
            """
            [SingleValueObject<string>]
            public sealed partial record Sku;
            """);

        result.ShouldHaveDiagnostic("DDD00013", at: "Sku");
        result.ShouldNotHaveGeneratedFor("Sku");
    }
}
