using DTS.DDD.Validation;

namespace DTS.DDD.Analyzers.Tests.Generation;

/// <summary>
/// The ValidatorGenerator turns the rules of a nested <c>Validator</c> into <c>if</c> statements in the
/// <c>Validate()</c> overrides of a value object. The failures are <see cref="ValidationError"/>s with a standard
/// code, or with the code of an error record from <c>WithError</c>.
/// </summary>
public class ValidatorGeneratorTests
{
    private const string Preamble = """
        using DTS.DDD.Abstractions.Attributes;
        using DTS.DDD.Results;
        using DTS.DDD.Validation;
        using System;
        using System.Collections.Generic;
        using System.Text.RegularExpressions;

        namespace Sample;


        """;

    private static GeneratorRunOutcome Run(string source)
        => GeneratorTestHost.Create(Preamble + source).RunCore();

    private static IReadOnlyList<ValidationError> Failures(EmittedAssembly emitted, object value)
        => (IReadOnlyList<ValidationError>)emitted.Property(value, "ValidationErrors")!;

    private const string EmailAddress = """
        [SingleValueObject<string>]
        public partial record EmailAddress
        {
            public static EmailAddress Create(string value) => new(value);

            partial class Validator
            {
                public Validator() => RuleFor(x => x.Value).NotEmpty().EmailAddress();
            }
        }
        """;

    [Fact]
    public void A_single_value_object_gets_a_check_and_both_Validate_overrides()
    {
        var result = Run(EmailAddress);

        result.ShouldCompile();
        result.ShouldContain(Hint.Of("Sample.EmailAddress", ".Validator"), "partial class Validator : global::DTS.DDD.Validation.Rules<global::Sample.EmailAddress>");
        result.ShouldContain(Hint.Of("Sample.EmailAddress", ".Validator"), "protected override bool Validate() => Validator.Check(this, null);");
        result.ShouldContain(Hint.Of("Sample.EmailAddress", ".Validator"), "protected override bool CollectsFailuresOnlyWhenInvalid => true;");
    }

    [Fact]
    public void Every_failed_rule_of_a_property_gives_a_failure_with_its_standard_code()
    {
        var emitted = Run(EmailAddress).Emit();

        var good = emitted.CallStatic("Sample.EmailAddress", "Create", "ada@harbor.example")!;
        var bad = emitted.CallStatic("Sample.EmailAddress", "Create", "harbor.example")!;
        var empty = emitted.CallStatic("Sample.EmailAddress", "Create", "")!;

        emitted.Property(good, "IsValid").Should().Be(true);
        Failures(emitted, good).Should().BeEmpty();

        emitted.Property(bad, "IsValid").Should().Be(false);
        var failure = Failures(emitted, bad).Should().ContainSingle().Subject;
        failure.Code.Should().Be(RuleFailures.EmailAddressCode);
        failure.PropertyName.Should().Be("Value");
        failure.AttemptedValue.Should().Be("harbor.example");

        Failures(emitted, empty).Select(f => f.Code).Should().Equal(RuleFailures.NotEmptyCode, RuleFailures.EmailAddressCode);
    }

    [Fact]
    public void Validating_a_valid_value_allocates_nothing()
    {
        var emitted = Run(EmailAddress).Emit();
        var warmUp = emitted.CallStatic("Sample.EmailAddress", "Create", "ada@harbor.example")!;
        emitted.Property(warmUp, "IsValid");
        var isValid = warmUp.GetType().GetProperty("IsValid")!.GetMethod!.CreateDelegate<Func<bool>>(
            emitted.CallStatic("Sample.EmailAddress", "Create", "rhea@harbor.example")!);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var valid = isValid();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        valid.Should().BeTrue();
        allocated.Should().Be(0);
    }

    [Fact]
    public void A_length_failure_carries_its_limits_as_arguments()
    {
        var emitted = Run(
            """
            [ValueObject]
            public partial record Address
            {
                public const int StreetLength = 40;

                public Address(string street) => Street = street;

                public string Street { get; protected init; }

                partial class Validator
                {
                    public Validator() => RuleFor(x => x.Street).Length(2, StreetLength);
                }
            }
            """).Emit();

        var tooLong = emitted.New("Sample.Address", new string('a', 52));

        var failure = Failures(emitted, tooLong).Should().ContainSingle().Subject;
        failure.Code.Should().Be(RuleFailures.LengthCode);
        failure.PropertyName.Should().Be("Street");
        failure.Arguments["MinLength"].Should().Be(2);
        failure.Arguments["MaxLength"].Should().Be(40);
        failure.Arguments["TotalLength"].Should().Be(52);
        failure.Message.Should().Be("'Street' must be between 2 and 40 characters. You entered 52 characters.");
    }

    [Fact]
    public void When_and_Unless_make_a_rule_depend_on_another_property()
    {
        var emitted = Run(
            """
            [ValueObject]
            public partial record Address
            {
                public Address(string country, string? postalCode, string? state)
                    => (Country, PostalCode, State) = (country, postalCode, state);

                public string Country { get; protected init; }
                public string? PostalCode { get; protected init; }
                public string? State { get; protected init; }

                partial class Validator
                {
                    public Validator()
                    {
                        RuleFor(x => x.Country).NotEmpty();
                        RuleFor(x => x.PostalCode).NotEmpty().When(x => x.Country == "NL");
                        RuleFor(x => x.State).NotEmpty().Unless(x => x.Country == "NL");
                    }
                }
            }
            """).Emit();

        Failures(emitted, emitted.New("Sample.Address", "NL", "1011 AB", null)).Should().BeEmpty();
        Failures(emitted, emitted.New("Sample.Address", "NL", null, null)).Should().ContainSingle().Which.PropertyName.Should().Be("PostalCode");
        Failures(emitted, emitted.New("Sample.Address", "US", null, null)).Should().ContainSingle().Which.PropertyName.Should().Be("State");
    }

    [Fact]
    public void A_group_When_applies_to_every_rule_inside_it()
    {
        var emitted = Run(
            """
            [ValueObject]
            public partial record Customer
            {
                public Customer(bool isBusiness, string? companyName, string? vatNumber)
                    => (IsBusiness, CompanyName, VatNumber) = (isBusiness, companyName, vatNumber);

                public bool IsBusiness { get; protected init; }
                public string? CompanyName { get; protected init; }
                public string? VatNumber { get; protected init; }

                private static readonly Regex VatPattern = new(@"^NL\d{9}B\d{2}$");

                partial class Validator
                {
                    public Validator()
                    {
                        When(x => x.IsBusiness, () =>
                        {
                            RuleFor(x => x.CompanyName).NotEmpty();
                            RuleFor(x => x.VatNumber).NotEmpty().Matches(VatPattern);
                        });
                    }
                }
            }
            """).Emit();

        Failures(emitted, emitted.New("Sample.Customer", false, null, null)).Should().BeEmpty();
        Failures(emitted, emitted.New("Sample.Customer", true, "Harbor Works", "NL123456789B01")).Should().BeEmpty();
        Failures(emitted, emitted.New("Sample.Customer", true, null, "NL12")).Select(f => f.Code)
            .Should().Equal(RuleFailures.NotEmptyCode, RuleFailures.MatchesCode);
    }

    [Fact]
    public void WithError_gives_the_failure_the_code_message_and_arguments_of_the_error_record()
    {
        var emitted = Run(
            """
            [ErrorCode("address.postal-code-required", "A postal code is required in {Country}.")]
            public sealed partial record PostalCodeRequired(string Country) : InvalidError;

            [ValueObject]
            public partial record Address
            {
                public Address(string country, string? postalCode) => (Country, PostalCode) = (country, postalCode);

                public string Country { get; protected init; }
                public string? PostalCode { get; protected init; }

                partial class Validator
                {
                    public Validator()
                        => RuleFor(x => x.PostalCode).NotEmpty().WithError(x => new PostalCodeRequired(x.Country)).When(x => x.Country == "NL");
                }
            }
            """).Emit();

        var failure = Failures(emitted, emitted.New("Sample.Address", "NL", null)).Should().ContainSingle().Subject;
        failure.Code.Should().Be("address.postal-code-required");
        failure.Message.Should().Be("A postal code is required in NL.");
        failure.PropertyName.Should().Be("PostalCode");
        failure.Arguments["Country"].Should().Be("NL");
    }

    [Fact]
    public void Must_takes_the_value_or_the_value_object_and_the_value()
    {
        var emitted = Run(
            """
            [ValueObject]
            public partial record Period
            {
                public Period(int from, int to) => (From, To) = (from, to);

                public int From { get; protected init; }
                public int To { get; protected init; }

                partial class Validator
                {
                    public Validator()
                    {
                        RuleFor(x => x.From).GreaterThanOrEqualTo(0).Must(from => from % 2 == 0);
                        RuleFor(x => x.To).Must((period, to) => to > period.From);
                    }
                }
            }
            """).Emit();

        Failures(emitted, emitted.New("Sample.Period", 2, 5)).Should().BeEmpty();
        Failures(emitted, emitted.New("Sample.Period", -1, -3)).Select(f => (f.PropertyName, f.Code)).Should().Equal(
            ("From", RuleFailures.GreaterThanOrEqualCode),
            ("From", RuleFailures.MustCode),
            ("To", RuleFailures.MustCode));
    }

    [Fact]
    public void RuleForEach_names_the_item_by_its_index()
    {
        var emitted = Run(
            """
            [ValueObject]
            public partial record Crew
            {
                public Crew(List<string> names) => Names = names;

                public List<string> Names { get; protected init; }

                partial class Validator
                {
                    public Validator() => RuleForEach(x => x.Names).NotEmpty();
                }
            }
            """).Emit();

        var crew = emitted.New("Sample.Crew", new List<string> { "Ada", "", "Leo" });

        Failures(emitted, crew).Should().ContainSingle().Which.PropertyName.Should().Be("Names[1]");
    }

    [Fact]
    public void A_property_that_holds_a_value_object_adds_its_failures_under_its_name()
    {
        var emitted = Run(
            """
            [SingleValueObject<string>]
            public partial record PostalCode
            {
                partial class Validator
                {
                    public Validator() => RuleFor(x => x.Value).Matches(@"^\d{4}\s?[A-Z]{2}$");
                }
            }

            [ValueObject]
            public partial record Address
            {
                public Address(string street, PostalCode postalCode) => (Street, PostalCode) = (street, postalCode);

                public string Street { get; protected init; }
                public PostalCode PostalCode { get; protected init; }

                partial class Validator
                {
                    public Validator() => RuleFor(x => x.Street).NotEmpty();
                }
            }
            """).Emit();

        var postalCode = emitted.New("Sample.PostalCode", "12");
        var address = emitted.New("Sample.Address", "Kade 1", postalCode);

        var failure = Failures(emitted, address).Should().ContainSingle().Subject;
        failure.PropertyName.Should().Be("PostalCode.Value");
        failure.Code.Should().Be(RuleFailures.MatchesCode);
    }

    [Fact]
    public void A_project_with_DTS_DDD_FluentValidation_keeps_that_generator()
    {
        var result = GeneratorTestHost.Create(Preamble.Replace("using DTS.DDD.Validation;", "using FluentValidation;") + EmailAddress)
            .WithFluentValidation()
            .RunCoreAnd(GeneratorTestHost.FluentValidationGenerators());

        result.ShouldCompile();
        result.GeneratedSources.Should().NotContain(source => source.HintName == Hint.Of("Sample.EmailAddress", ".Validator"));
    }

    [Theory]
    [InlineData("RuleFor(x => x.Value).NotEmpty().WithMessage(\"Required\");", "DDD00115", "WithMessage")]
    [InlineData("RuleFor(x => x.Value.Trim()).NotEmpty();", "DDD00116", "RuleFor")]
    [InlineData("RuleFor(x => x.Value).Must(v => { return v.Length > 0; });", "DDD00116", "Must")]
    [InlineData("RuleFor(x => x.Value).NotEmpty().WithError(x => new Unrelated());", "DDD00117", "new Unrelated()")]
    [InlineData("var limit = 3; RuleFor(x => x.Value).MaximumLength(limit);", "DDD00118", "var limit = 3;")]
    public void A_rule_the_generator_cannot_copy_gets_a_diagnostic(string rules, string id, string at)
    {
        var result = Run(
            $$"""
            public sealed record Unrelated : InvalidError
            {
                public Unrelated() : base("sample.unrelated", "Unrelated.") { }
            }

            [SingleValueObject<string>]
            public partial record Code
            {
                partial class Validator
                {
                    public Validator()
                    {
                        {{rules}}
                    }
                }
            }
            """);

        result.ShouldHaveDiagnostic(id, at);
    }

    [Fact]
    public void A_rule_that_uses_a_local_of_the_constructor_gets_DDD00119()
    {
        var result = Run(
            """
            [SingleValueObject<string>]
            public partial record Code
            {
                partial class Validator
                {
                    private readonly int _limit = 3;

                    public Validator() => RuleFor(x => x.Value).MaximumLength(_limit);
                }
            }
            """);

        result.ShouldHaveDiagnostic("DDD00119", "_limit");
    }
}
