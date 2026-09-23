using DDDToolkit.Exceptions;
using DDDToolkit.FluentValidation;
using DDDToolkit.Invariants;
using DDDToolkit.Tests.Domain;
using DDDToolkit.Tests.Validation;
using DDDToolkit.Validation;
using FluentAssertions;
using FluentValidation;

namespace DDDToolkit.Tests.Localization;

/// <summary>
/// The values a message was built from travel with the failure, separately from the message, all the
/// way from the rule to whoever turns it into a response. That is the whole precondition for phrasing it
/// again in another language: a sentence with the number already inside it cannot be translated.
/// </summary>
public class FailureArgumentsTests
{
    // ---------------------------------------------------------------- ValidationError

    [Fact]
    public void A_failure_without_arguments_has_an_empty_set_rather_than_null()
    {
        new ValidationError("anything").Arguments.Should().BeEmpty();
    }

    [Fact]
    public void Arguments_are_matched_without_regard_to_case()
    {
        var error = new ValidationError("too long", "Value", "TooLong").With("MaxLength", 40);

        error.Arguments["maxlength"].Should().Be(40);
    }

    [Fact]
    public void Arguments_are_copied_so_the_callers_dictionary_can_change_afterwards()
    {
        var arguments = new Dictionary<string, object?> { ["MaxLength"] = 40 };

        var error = new ValidationError("too long", arguments: arguments);
        arguments["MaxLength"] = 99;

        error.Arguments["MaxLength"].Should().Be(40);
    }

    [Fact]
    public void Failures_with_the_same_arguments_are_equal_however_they_were_built()
    {
        var one = new ValidationError("x", "P", "C").With("A", 1).With("B", "two");
        var other = new ValidationError("x", "P", "C", arguments: new Dictionary<string, object?> { ["b"] = "two", ["a"] = 1 });

        one.Should().Be(other);
        one.GetHashCode().Should().Be(other.GetHashCode());
        one.Should().NotBe(one.With("A", 2));
    }

    [Fact]
    public void The_builder_takes_arguments_too()
    {
        var builder = new ValidationErrorBuilder();

        builder.Add("A street is at most 40 characters.", "Value", "TooLong", "x", new Dictionary<string, object?> { ["MaxLength"] = 40 });

        builder.ToReadOnlyList().Single().Arguments["MaxLength"].Should().Be(40);
    }

    [Fact]
    public void A_refusal_that_says_nothing_still_names_the_value_object()
    {
        var error = new Mystery().ValidationErrors.Should().ContainSingle().Which;

        error.Code.Should().Be(ValidationError.UnspecifiedCode);
        error.Arguments[ValidationError.ValueObjectArgument].Should().Be(nameof(Mystery));
    }

    // ---------------------------------------------------------------- FluentValidation

    [Fact]
    public void A_generated_validator_hands_its_placeholder_values_on()
    {
        var address = new Address("Main Street", new string('x', 50));

        var error = address.ValidationErrors.Should().ContainSingle().Which;

        error.Code.Should().Be("MaximumLengthValidator");
        error.Arguments["MaxLength"].Should().Be(20);
        error.Arguments["TotalLength"].Should().Be(50);
    }

    [Fact]
    public void A_validator_you_wrote_yourself_hands_them_on_as_well()
    {
        var validator = new InlineValidator<PlaceOrder>();
        validator.RuleFor(x => x.Quantity).GreaterThan(0);

        var error = validator.Validate(new PlaceOrder(null, null, 0)).ToValidationErrors().Single();

        error.Arguments["ComparisonValue"].Should().Be(0);
    }

    [Fact]
    public void MustBeValid_names_the_value_object_as_an_argument_and_still_reads_the_same()
    {
        var validator = new InlineValidator<PlaceOrder>();
        validator.RuleFor(x => x.Product).MustBeValid();

        var error = validator.Validate(new PlaceOrder(Slug.Create("Not A Slug"), null, 1)).ToValidationErrors().Single();

        error.Message.Should().Be("'Product' is not a valid Slug.");
        error.Arguments[ValidationError.ValueObjectArgument].Should().Be(nameof(Slug));
    }

    // ---------------------------------------------------------------- invariants

    [Fact]
    public void A_string_still_converts_to_a_failure_and_null_still_means_it_holds()
    {
        InvariantFailure? broken = "A message.";
        InvariantFailure? holds = (string?)null;

        broken!.Message.Should().Be("A message.");
        broken.Arguments.Should().BeEmpty();
        holds.Should().BeNull();
    }

    [Fact]
    public void A_rules_arguments_reach_the_violation_it_is_reported_as()
    {
        var till = new Till(TillId.CreateUnique(), limit: 100m);
        till.Deposit(150m);

        var violation = till.GetInvariantViolations().Should().ContainSingle().Which;

        violation.Code.Should().Be(Till.MustStayUnderLimit.ViolationCode);
        violation.Arguments["Limit"].Should().Be(100m);
        violation.Arguments["Cash"].Should().Be(150m);
    }

    [Fact]
    public void The_throwing_path_keeps_every_violation_whole()
    {
        var till = new Till(TillId.CreateUnique(), limit: 100m);
        till.Deposit(150m);
        var drawer = till.AddDrawer(new DrawerId(1), coins: -3);

        var exception = FluentActions.Invoking(till.EnsureInvariants)
            .Should().Throw<InvariantViolationException>().Which;

        exception.InvariantViolations.Select(v => v.Code)
            .Should().Equal(Till.MustStayUnderLimit.ViolationCode, Drawer.MustNotBeNegative.ViolationCode);
        exception.InvariantViolations[1].EntityType.Should().Be<Drawer>();
        exception.InvariantViolations[1].EntityId.Should().Be(drawer.Id);
        exception.InvariantViolations[1].Arguments["Coins"].Should().Be(-3);

        // The phrased list reads as it always did: the till's own rule bare, the drawer's with its name.
        exception.Violations[0].Should().StartWith("A till holds at most 100");
        exception.Violations[1].Should().StartWith("Drawer ");
    }

    [Fact]
    public void A_rule_given_only_as_a_string_is_reported_with_the_seam_code()
    {
        var exception = new InvariantViolationException(typeof(Till), null, ["First.", "Second."]);

        exception.InvariantViolations.Select(v => v.Code).Should().OnlyContain(code => code == InvariantViolation.SeamCode);
        exception.InvariantViolations.Select(v => v.Message).Should().Equal("First.", "Second.");
    }

    [Fact]
    public void Violations_compare_by_their_arguments_too()
    {
        var violation = new InvariantViolation("C", "M").With("Limit", 1);

        violation.Should().Be(new InvariantViolation("C", "M").With("limit", 1));
        violation.Should().NotBe(new InvariantViolation("C", "M"));
    }
}
