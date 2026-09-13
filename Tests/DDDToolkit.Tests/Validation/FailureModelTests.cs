using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Exceptions;
using DDDToolkit.FluentValidation;
using DDDToolkit.Tests.Domain;
using DDDToolkit.Validation;
using FluentAssertions;
using FluentValidation;

namespace DDDToolkit.Tests.Validation;

/// <summary>
/// The failure model that is not an exception.
/// <para>
/// DECISION (pinned here): the toolkit ships the Try pattern, not a <c>Result&lt;T&gt;</c>.
/// <c>TryToValid()</c> hands back the twin and the failures; what you put them in is your business, so a
/// codebase already using FluentResults, ErrorOr or its own result type is not asked to adopt a fourth.
/// The throwing path is untouched: <c>ToValid()</c> and <c>EnsureValidated()</c> still throw.
/// </para>
/// </summary>
public class FailureModelTests
{
    // ---------------------------------------------------------------- the non-throwing conversion

    [Fact]
    public void AValidSingleValueObjectConvertsWithoutThrowing()
    {
        var email = EmailAddress.Create("ada@example.com");

        var converted = email.TryToValid(out var valid, out var errors);

        converted.Should().BeTrue();
        valid.Should().BeOfType<ValidEmailAddress>().Which.Value.Should().Be("ada@example.com");
        errors.Should().BeEmpty();
    }

    [Fact]
    public void AnInvalidSingleValueObjectReportsWhyInsteadOfThrowing()
    {
        var email = EmailAddress.Create("nope");

        var converted = email.TryToValid(out var valid, out var errors);

        converted.Should().BeFalse();
        valid.Should().BeNull();
        var error = errors.Should().ContainSingle().Which;
        error.PropertyName.Should().Be("Value");
        error.Code.Should().Be("RegularExpressionValidator");
        error.AttemptedValue.Should().Be("nope");
        error.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void AMultiPropertyValueObjectReportsEveryFailureNotJustTheFirst()
    {
        var address = new Address("", new string('x', 50));

        address.TryToValid(out ValidAddress? valid, out var errors).Should().BeFalse();

        valid.Should().BeNull();
        errors.Select(e => e.PropertyName).Should().BeEquivalentTo("Street", "City");
        errors.Select(e => e.Code).Should().BeEquivalentTo("NotEmptyValidator", "MaximumLengthValidator");
    }

    [Fact]
    public void AReferenceIdentifierConvertsTheSameWay()
    {
        var known = TicketId.Create(Guid.NewGuid());
        var missing = TicketId.Create(Guid.Empty);

        known.TryToValid(out var valid, out _).Should().BeTrue();
        valid.Should().BeOfType<ValidTicketId>();

        missing.TryToValid(out var none, out var errors).Should().BeFalse();
        none.Should().BeNull();
        errors.Should().ContainSingle().Which.Code.Should().Be("NoSuchTicket");
    }

    [Fact]
    public void TheShortOverloadIsForCallersThatDoNotCareWhy()
    {
        EmailAddress.Create("ada@example.com").TryToValid(out var valid).Should().BeTrue();
        valid.Should().NotBeNull();

        EmailAddress.Create("nope").TryToValid(out var none).Should().BeFalse();
        none.Should().BeNull();
    }

    [Fact]
    public void ConvertingTwiceGivesTwoEqualTwins()
    {
        var email = EmailAddress.Create("ada@example.com");

        email.TryToValid(out var first, out _);
        email.TryToValid(out var second, out _);

        first.Should().NotBeSameAs(second);
        first.Should().Be(second, "a value object is its contents");
    }

    [Fact]
    public void TryValidateChecksAValueYouDoNotIntendToConvert()
    {
        new Address("1 Main St", "Delft").TryValidate(out var none).Should().BeTrue();
        none.Should().BeEmpty();

        new Address("", "Delft").TryValidate(out var errors).Should().BeFalse();
        errors.Should().ContainSingle().Which.PropertyName.Should().Be("Street");
    }

    // ---------------------------------------------------------------- the throwing path still throws

    [Fact]
    public void ToValidStillThrows()
    {
        var act = () => EmailAddress.Create("nope").ToValid();

        act.Should().Throw<InvalidValueObjectException>();
    }

    [Fact]
    public void TheThrownExceptionNowCarriesTheSameFailures()
    {
        var address = new Address("", "Delft");

        var exception = address.Invoking(a => a.ToValid()).Should().Throw<InvalidValueObjectException>().Which;

        exception.ObjectType.Should().Be<Address>();
        exception.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("Street");
        exception.Message.Should().StartWith("The value object Address is invalid.")
            .And.Contain("Street", "the message repeats the reasons so a log line is useful on its own");
    }

    [Fact]
    public void AnExceptionBuiltWithoutDetailStillReadsAsItAlwaysDid()
    {
        var exception = new InvalidValueObjectException(typeof(Address));

        exception.Message.Should().Be("The value object Address is invalid.");
        exception.Errors.Should().BeEmpty();
    }

    // ---------------------------------------------------------------- ValidationErrors

    [Fact]
    public void ValidationErrorsRunsTheRulesOnFirstRead()
    {
        var address = new Address("", "Delft");

        address.IsValidated.Should().BeFalse();

        address.ValidationErrors.Should().ContainSingle(
            "reading the failures is asking for the verdict, so nothing has to touch IsValid first");
        address.IsValidated.Should().BeTrue();
    }

    [Fact]
    public void AValidValueHasNoFailures()
    {
        new Address("1 Main St", "Delft").ValidationErrors.Should().BeEmpty();
    }

    [Fact]
    public void TheAlwaysValidTwinHasNoFailures()
    {
        var twin = EmailAddress.Create("ada@example.com").ToValid();

        twin.IsValid.Should().BeTrue();
        twin.ValidationErrors.Should().BeEmpty();
    }

    [Fact]
    public void TheFailuresAreComputedOnceAndCachedWithTheVerdict()
    {
        var counted = new Counted { Text = string.Empty };
        var before = FailureModelCounter.Runs;

        counted.ValidationErrors.Should().ContainSingle();
        counted.ValidationErrors.Should().ContainSingle();
        counted.TryValidate(out _).Should().BeFalse();
        _ = counted.IsValid;

        FailureModelCounter.Runs.Should().Be(before + 1, "the rules run on the first read and never again");
    }

    // ---------------------------------------------------------------- without FluentValidation

    [Fact]
    public void AToolkitOnlyValueObjectDescribesItsOwnFailures()
    {
        var reading = new Temperature(-300);

        reading.TryValidate(out var errors).Should().BeFalse();
        var error = errors.Should().ContainSingle().Which;
        error.Message.Should().Be("A temperature cannot be below absolute zero.");
        error.PropertyName.Should().Be("Celsius");
        error.Code.Should().Be("BelowAbsoluteZero");
        error.AttemptedValue.Should().Be(-300);
    }

    [Fact]
    public void AToolkitOnlyValueObjectReportsEveryRuleThatFailed()
    {
        // The builder collects; it does not stop at the first refusal.
        var nonsense = new Temperature(5000, label: string.Empty);

        nonsense.ValidationErrors.Select(e => e.Code)
            .Should().BeEquivalentTo("OutOfRange", "LabelRequired");
        new Temperature(20).ValidationErrors.Should().BeEmpty();
    }

    [Fact]
    public void ABareValidateThatRefusesWithoutSayingWhyStillProducesAFailure()
    {
        var mystery = new Mystery();

        mystery.TryValidate(out var errors).Should().BeFalse();
        var error = errors.Should().ContainSingle().Which;
        error.Code.Should().Be(ValidationError.UnspecifiedCode);
        error.Message.Should().Be("Mystery is not valid.");
        error.PropertyName.Should().BeNull();
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public void BothHalvesOfTheRulesMustAgree(bool bolt, bool described, bool expected)
    {
        var padlock = new Padlock { BoltAccepts = bolt, DescribedRuleAccepts = described };

        padlock.IsValid.Should().Be(expected);
    }

    [Fact]
    public void AFailureFromEitherHalfIsReported()
    {
        new Padlock { BoltAccepts = true, DescribedRuleAccepts = false }
            .ValidationErrors.Should().ContainSingle().Which.Code.Should().Be("Described");

        new Padlock { BoltAccepts = false, DescribedRuleAccepts = true }
            .ValidationErrors.Should().ContainSingle().Which.Code.Should().Be(ValidationError.UnspecifiedCode);

        new Padlock { BoltAccepts = false, DescribedRuleAccepts = false }
            .ValidationErrors.Should().ContainSingle(
                "a described failure already explains the refusal, so no placeholder is added on top")
            .Which.Code.Should().Be("Described");
    }

    // ---------------------------------------------------------------- what implements what

    [Fact]
    public void AllThreeFamiliesOfferTheNonThrowingConversion()
    {
        // Compile-time, not reflection: each of these binds the extension method on IValidatable<T>.
        IValidatable<ValidEmailAddress> single = EmailAddress.Create("ada@example.com");
        IValidatable<ValidPersonName> multi = new PersonName("Ada", "Lovelace");
        IValidatable<ValidTicketId> identifier = TicketId.Create(Guid.NewGuid());

        single.TryToValid(out _).Should().BeTrue();
        multi.TryToValid(out _).Should().BeTrue();
        identifier.TryToValid(out _).Should().BeTrue();
    }

    [Fact]
    public void StructIdentifiersHaveNoTwinAndSoNoConversion()
    {
        typeof(CatId).GetInterfaces()
            .Should().NotContain(
                type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IValidatable<>),
                "a struct id is well formed by construction and has nothing to convert to");
    }

    // ---------------------------------------------------------------- shaping a refusal

    [Fact]
    public void FailuresFromSeveralValueObjectsCanBeLabelledAndMerged()
    {
        var email = EmailAddress.Create("nope");
        var shipTo = new Address("", new string('x', 50));

        email.TryToValid(out _, out var emailErrors);
        shipTo.TryToValid(out ValidAddress? _, out var addressErrors);

        var all = emailErrors.Prefixed("email").Concat(addressErrors.Prefixed("shipTo")).ToList();

        all.Select(e => e.PropertyName)
            .Should().BeEquivalentTo("email.Value", "shipTo.Street", "shipTo.City");
    }

    [Fact]
    public void PrefixingLeavesTheOriginalsAlone()
    {
        var errors = new Temperature(-300).ValidationErrors;

        var prefixed = errors.Prefixed("reading");

        prefixed[0].PropertyName.Should().Be("reading.Celsius");
        prefixed[0].Code.Should().Be("BelowAbsoluteZero", "only the label changes");
        errors[0].PropertyName.Should().Be("Celsius");
    }

    [Fact]
    public void AFailureWithNoPropertyTakesThePrefixAsItsName()
    {
        var prefixed = new Mystery().ValidationErrors.Prefixed("payload");

        prefixed.Should().ContainSingle().Which.PropertyName.Should().Be("payload");
    }

    [Fact]
    public void ToErrorDictionaryGivesTheShapeAValidationProblemWants()
    {
        var address = new Address("", new string('x', 50));
        address.TryToValid(out ValidAddress? _, out var errors);

        var dictionary = errors.ToErrorDictionary();

        dictionary.Keys.Should().BeEquivalentTo("Street", "City");
        dictionary["Street"].Should().ContainSingle();
    }

    [Fact]
    public void SeveralFailuresOnOnePropertyEndUpUnderOneKey()
    {
        var errors = new[]
        {
            new ValidationError("too short", "Value"),
            new ValidationError("wrong shape", "Value"),
            new ValidationError("the request makes no sense"),
        };

        var dictionary = errors.ToErrorDictionary();

        dictionary["Value"].Should().BeEquivalentTo("too short", "wrong shape");
        dictionary[ValidationErrorExtensions.NoProperty].Should().ContainSingle();
    }

    // ---------------------------------------------------------------- the FluentValidation bridge

    private sealed class PlaceOrderValidator : AbstractValidator<PlaceOrder>
    {
        public PlaceOrderValidator()
        {
            RuleFor(x => x.Product).NotNull().MustBeValid();
            RuleFor(x => x.Quantity).GreaterThan(0);
        }
    }

    [Fact]
    public void AValidatorYouWroteYourselfJoinsTheSameList()
    {
        var result = new PlaceOrderValidator().Validate(
            new PlaceOrder(Slug.Create("Flat White"), ShipTo: null, Quantity: 0));

        var errors = result.ToValidationErrors();

        errors.Select(e => e.PropertyName).Should().BeEquivalentTo("Product", "Quantity");
        errors.Single(e => e.PropertyName == "Product").Code.Should().Be(ValueObjectRules.ErrorCode);
        errors.ToErrorDictionary().Keys.Should().BeEquivalentTo("Product", "Quantity");
    }

    [Fact]
    public void APassingValidatorConvertsToNoFailures()
    {
        var result = new PlaceOrderValidator().Validate(
            new PlaceOrder(Slug.Create("flat-white"), ShipTo: null, Quantity: 2));

        result.ToValidationErrors().Should().BeEmpty();
    }

    // ---------------------------------------------------------------- ValidationError itself

    [Fact]
    public void AFailureReadsAsPropertyThenMessage()
    {
        new ValidationError("must not be empty", "Street").ToString().Should().Be("Street: must not be empty");
        new ValidationError("the request makes no sense").ToString().Should().Be("the request makes no sense");
    }

    [Fact]
    public void FailuresCompareByContents()
    {
        new ValidationError("a", "B", "C").Should().Be(new ValidationError("a", "B", "C"));
        new ValidationError("a", "B", "C").Should().NotBe(new ValidationError("a", "B", "D"));
    }
}
