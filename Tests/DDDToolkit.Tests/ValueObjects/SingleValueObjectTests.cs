using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Exceptions;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Tests.Domain;
using FluentAssertions;

namespace DDDToolkit.Tests.ValueObjects;

/// <summary>
/// <c>[SingleValueObject&lt;T&gt;]</c>: one wrapped value, structural equality over it, and an
/// always-valid twin whose constructor is the validating gate.
/// </summary>
public class SingleValueObjectTests
{
    // ---------------------------------------------------------------- equality and the wrapped value

    [Fact]
    public void EqualityIsByTheWrappedValue()
    {
        var email = EmailAddress.Create("test@example.com");
        var same = EmailAddress.Create("test@example.com");

        same.Should().NotBeSameAs(email);
        (email == same).Should().BeTrue();
        email.GetHashCode().Should().Be(same.GetHashCode());
        (email == EmailAddress.Create("other@example.com")).Should().BeFalse();
    }

    [Fact]
    public void TheValueIsReadableBothTypedAndUntyped()
    {
        var email = EmailAddress.Create("test@example.com");

        email.Value.Should().Be("test@example.com");
        ((ISingleValueObject)email).GetValue().Should().Be("test@example.com");
        email.Should().BeAssignableTo<SingleValueObject<string>>();
        email.Should().BeAssignableTo<IValueObject>();
    }

    [Fact]
    public void SingleValueObjectsWorkAsDictionaryKeys()
    {
        var byEmail = new Dictionary<EmailAddress, string>
        {
            [EmailAddress.Create("test@example.com")] = "ada",
        };

        byEmail[EmailAddress.Create("test@example.com")].Should().Be("ada");
        byEmail.ContainsKey(EmailAddress.Create("other@example.com")).Should().BeFalse();
    }

    // ---------------------------------------------------------------- validation

    [Fact]
    public void ValidationFollowsTheGeneratedValidator()
    {
        EmailAddress.Create("test@example.com").IsValid.Should().BeTrue();
        EmailAddress.Create("testexample.com").IsValid.Should().BeFalse();
        EmailAddress.Create("").IsValid.Should().BeFalse();
    }

    [Fact]
    public void AValueObjectWithoutRulesIsAlwaysValid()
    {
        // DateOfBirth declares no rules, so the generated Validator has none and every value passes.
        var dateOfBirth = new ValidDateOfBirth(new DateOnly(1990, 1, 1));

        dateOfBirth.IsValid.Should().BeTrue();
        dateOfBirth.Value.Should().Be(new DateOnly(1990, 1, 1));
    }

    [Fact]
    public void EnsureValidatedThrowsOnAnInvalidValue()
    {
        var invalid = EmailAddress.Create("testexample.com");

        var act = () => invalid.EnsureValidated();

        act.Should().Throw<InvalidValueObjectException>().WithMessage("*EmailAddress*");
    }

    [Fact]
    public void EnsureValidatedIsSilentOnAValidValue()
    {
        var valid = EmailAddress.Create("test@example.com");

        var act = () => valid.EnsureValidated();

        act.Should().NotThrow();
    }

    // ---------------------------------------------------------------- the always-valid twin

    [Fact]
    public void ToValidReturnsTheTwinForAValidValue()
    {
        var email = EmailAddress.Create("test@example.com");

        var valid = email.ToValid();

        valid.Should().BeOfType<ValidEmailAddress>();
        valid.Should().BeAssignableTo<IAlwaysValid>();
        valid.Should().BeAssignableTo<EmailAddress>();
        valid.Value.Should().Be("test@example.com");
        valid.IsValidated.Should().BeTrue();
    }

    [Fact]
    public void ToValidThrowsForAnInvalidValue()
    {
        var email = EmailAddress.Create("testexample.com");

        var act = () => email.ToValid();

        act.Should().Throw<InvalidValueObjectException>().WithMessage("*EmailAddress*");
    }

    [Fact]
    public void TheTwinCanBeBuiltStraightFromTheRawValue()
    {
        var valid = new ValidEmailAddress("test@example.com");

        valid.Value.Should().Be("test@example.com");
        valid.Should().Be(new ValidEmailAddress("test@example.com"));
    }

    [Fact]
    public void TheTwinRefusesToBeBuiltFromAnInvalidRawValue()
    {
        var act = () => new ValidEmailAddress("testexample.com");

        act.Should().Throw<InvalidValueObjectException>();
    }

    [Fact]
    public void TheTwinIsTheTypeThatProvesValidityToACaller()
    {
        // A method that takes a ValidEmailAddress cannot be handed an unvalidated one, which is the
        // point: the check moves from the call site into the type system.
        static string Send(ValidEmailAddress to) => $"sent to {to.Value}";

        Send(EmailAddress.Create("test@example.com").ToValid()).Should().Be("sent to test@example.com");
    }

    [Fact]
    public void TwinEqualityIsAsymmetricAgainstItsSource()
    {
        // DECISION (pinned): the twin is a derived record, and C# record equality is asymmetric across
        // an inheritance step — the derived type's Equals rejects a base instance, while the base's
        // accepts anything with the same components. Compare Values, or compare twins to twins; do not
        // mix the two kinds in a `==`.
        var email = EmailAddress.Create("test@example.com");
        var valid = email.ToValid();

        valid.Value.Should().Be(email.Value);
        (email == valid).Should().BeTrue("EmailAddress.Equals only looks at the components");
        (valid == email).Should().BeFalse("ValidEmailAddress.Equals additionally requires a ValidEmailAddress");
    }

    // ---------------------------------------------------------------- locally declared single value objects

    [Fact]
    public void LocallyDeclaredSingleValueObjectsBehaveTheSame()
    {
        Slug.Create("hello-world").IsValid.Should().BeTrue();
        Slug.Create("Hello World").IsValid.Should().BeFalse("the rule allows lower case, digits and hyphens only");

        Slug.Create("hello-world").ToValid().Value.Should().Be("hello-world");
        var act = () => Slug.Create("Hello World").ToValid();
        act.Should().Throw<InvalidValueObjectException>();
    }
}
