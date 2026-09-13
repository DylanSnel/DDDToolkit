using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.FluentValidation;
using DDDToolkit.Tests.Domain;
using FluentAssertions;
using FluentValidation;

namespace DDDToolkit.Tests.Validation;

/// <summary>
/// The DDDToolkit.FluentValidation generator: every value object gains a nested <c>Validator</c>, an
/// <c>Errors</c> collection and a <c>Validate()</c> that runs them. These pin what a caller reads off a
/// failed value object, the fact that a verdict is computed once, and that
/// <see cref="ValueObjectRules.MustBeValid{T, TValueObject}"/> folds a value object into a validator of
/// your own.
/// </summary>
public class ValueObjectValidationTests
{
    // ---------------------------------------------------------------- Errors

    [Fact]
    public void ASingleValueObjectReportsFailuresAgainstValue()
    {
        var email = EmailAddress.Create("testexample.com");

        email.IsValid.Should().BeFalse();
        email.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be("Value", "the rule is declared as RuleFor(x => x.Value)");
    }

    [Fact]
    public void AValidValueHasNoErrors()
    {
        var email = EmailAddress.Create("test@example.com");

        email.IsValid.Should().BeTrue();
        email.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AMultiPropertyValueObjectReportsFailuresPerProperty()
    {
        var nameless = new PersonName("", "");

        nameless.IsValid.Should().BeFalse();
        nameless.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo("FirstName", "LastName");
    }

    [Fact]
    public void OnlyTheFailingPropertyIsReported()
    {
        var noSurname = new PersonName("John", "");

        noSurname.IsValid.Should().BeFalse();
        noSurname.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be("LastName");
    }

    [Fact]
    public void ErrorsAreEmptyUntilSomethingAsksForTheVerdict()
    {
        var nameless = new PersonName("", "");

        nameless.IsValidated.Should().BeFalse();
        nameless.Errors.Should().BeEmpty("nothing has run the validator yet");

        _ = nameless.IsValid;

        nameless.IsValidated.Should().BeTrue();
        nameless.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void ErrorsCarryTheValidatorsErrorCodeAndMessage()
    {
        var noSurname = new PersonName("John", "");
        _ = noSurname.IsValid;
        var missingEmail = EmailAddress.Create("testexample.com");
        _ = missingEmail.IsValid;

        noSurname.Errors[0].ErrorCode.Should().Be("NotEmptyValidator");
        noSurname.Errors[0].ErrorMessage.Should().Contain("Last Name", "FluentValidation splits PascalCase for display");
        missingEmail.Errors[0].ErrorCode.Should().Be("RegularExpressionValidator");
    }

    [Fact]
    public void ErrorsFromValueObjectsDeclaredInThisProjectBehaveTheSame()
    {
        var address = new Address("", new string('x', 50));

        address.IsValid.Should().BeFalse();
        address.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo("Street", "City");
        address.Errors.Select(e => e.ErrorCode)
            .Should().BeEquivalentTo("NotEmptyValidator", "MaximumLengthValidator");
    }

    [Fact]
    public void ErrorsIsAReadOnlyViewAndIsNotPersisted()
    {
        var email = EmailAddress.Create("testexample.com");
        _ = email.IsValid;

        (email.Errors as IList<global::FluentValidation.Results.ValidationFailure>)!.IsReadOnly
            .Should().BeTrue();

        var errors = typeof(EmailAddress).GetProperty("Errors")!;
        errors.GetCustomAttribute<NotMappedAttribute>().Should().NotBeNull("EF Core must not try to map it");
        errors.GetCustomAttribute<InternalAttribute>().Should().NotBeNull("serializers must not expose it");
    }

    // ---------------------------------------------------------------- the generated validator

    [Fact]
    public void TheValidatorIsANestedPrivateAbstractValidator()
    {
        var validator = typeof(Slug).GetNestedType("Validator", BindingFlags.NonPublic);

        validator.Should().NotBeNull("the generator nests it so the rules live with the value object");
        validator!.BaseType!.GetGenericTypeDefinition().Should().Be(typeof(AbstractValidator<>));
        validator.BaseType.GenericTypeArguments.Should().Equal(typeof(Slug));
    }

    [Fact]
    public void StructIdsGetNoValidatorBecauseTheyAreValidByConstruction()
    {
        typeof(CatId).GetNestedType("Validator", BindingFlags.NonPublic | BindingFlags.Public)
            .Should().BeNull();
        typeof(BasketId).GetProperty("Errors").Should().BeNull();

        typeof(PersonId).GetNestedType("Validator", BindingFlags.NonPublic)
            .Should().NotBeNull("reference ids are value objects and do get one");
    }

    // ---------------------------------------------------------------- caching

    [Fact]
    public void TheVerdictIsComputedOnceAndCached()
    {
        var value = CountedValue.Create("something");
        value.IsValidated.Should().BeFalse();

        var before = ValidationCounter.Runs;

        value.IsValid.Should().BeTrue();
        ValidationCounter.Runs.Should().Be(before + 1, "the first read runs the validator");

        value.IsValid.Should().BeTrue();
        value.IsValid.Should().BeTrue();
        value.EnsureValidated();
        value.ToValid();

        ValidationCounter.Runs.Should().Be(before + 1, "every later read comes from the cached field");
        value.IsValidated.Should().BeTrue();
    }

    [Fact]
    public void EachInstanceIsValidatedSeparately()
    {
        var first = CountedValue.Create("a");
        var second = CountedValue.Create("b");
        var before = ValidationCounter.Runs;

        _ = first.IsValid;
        _ = second.IsValid;

        ValidationCounter.Runs.Should().Be(before + 2, "the cache is per instance, not per type");
    }

    // ---------------------------------------------------------------- MustBeValid

    private sealed class PlaceOrderValidator : AbstractValidator<PlaceOrder>
    {
        public PlaceOrderValidator()
        {
            RuleFor(x => x.Product).NotNull().MustBeValid();
            RuleFor(x => x.ShipTo).MustBeValid();
            RuleFor(x => x.Quantity).GreaterThan(0);
        }
    }

    [Fact]
    public void MustBeValidPassesAWellFormedCommand()
    {
        var result = new PlaceOrderValidator().Validate(
            new PlaceOrder(Slug.Create("flat-white"), new Address("1 Main St", "Delft"), 2));

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void MustBeValidReportsTheValueObjectAgainstTheContainingProperty()
    {
        var result = new PlaceOrderValidator().Validate(
            new PlaceOrder(Slug.Create("Flat White"), ShipTo: null, Quantity: 1));

        result.IsValid.Should().BeFalse();
        var failure = result.Errors.Should().ContainSingle().Which;
        failure.PropertyName.Should().Be("Product");
        failure.ErrorCode.Should().Be(ValueObjectRules.ErrorCode);
        failure.ErrorMessage.Should().Be("'Product' is not a valid Slug.");
    }

    [Fact]
    public void MustBeValidLetsNullThrough()
    {
        // Null-ness is NotNull()'s job, exactly as with FluentValidation's own rules.
        var result = new PlaceOrderValidator().Validate(
            new PlaceOrder(Product: null, ShipTo: null, Quantity: 1));

        result.Errors.Should().ContainSingle("only the NotNull on Product fires")
            .Which.ErrorCode.Should().Be("NotNullValidator");
    }

    [Fact]
    public void MustBeValidWorksForMultiPropertyValueObjectsToo()
    {
        var result = new PlaceOrderValidator().Validate(
            new PlaceOrder(Slug.Create("flat-white"), new Address("", "Delft"), 1));

        result.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be("ShipTo");
    }

    [Fact]
    public void MustBeValidComposesWithOrdinaryRules()
    {
        var result = new PlaceOrderValidator().Validate(
            new PlaceOrder(Slug.Create("Flat White"), new Address("", "Delft"), 0));

        result.Errors.Select(e => e.PropertyName)
            .Should().BeEquivalentTo("Product", "ShipTo", "Quantity");
    }

    // ---------------------------------------------------------------- FluentValidation 12 behaviour
    //
    // The upgrade from 11.x moved a major version; these pin the two rules the toolkit's own examples
    // depend on, so a future bump shows up here rather than in somebody's domain model.

    private sealed record Sample(string? Text);

    private sealed class NotEmptySample : AbstractValidator<Sample>
    {
        public NotEmptySample() => RuleFor(x => x.Text).NotEmpty();
    }

    private sealed class MatchesSample : AbstractValidator<Sample>
    {
        public MatchesSample() => RuleFor(x => x.Text).Matches("^[a-z]+$");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("x", true)]
    [InlineData(" x ", true)]
    public void NotEmptyTreatsNullEmptyAndWhitespaceAsEmpty(string? text, bool expected)
    {
        new NotEmptySample().Validate(new Sample(text)).IsValid.Should().Be(expected);
    }

    [Theory]
    [InlineData("abc", true)]
    [InlineData("abC", false)]
    [InlineData("", false)]
    [InlineData(null, true)]
    public void MatchesTestsTheWholePatternAndIgnoresNull(string? text, bool expected)
    {
        // DECISION (pinned): Matches lets null through — FluentValidation's convention is that null is
        // NotNull()'s business. EmailAddress relies on this: an unset email is not a malformed one.
        new MatchesSample().Validate(new Sample(text)).IsValid.Should().Be(expected);
    }

    [Fact]
    public void RuleForStillReportsThePropertyPathItAlwaysDid()
    {
        var result = new NotEmptySample().Validate(new Sample(""));

        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("Text");
    }
}
