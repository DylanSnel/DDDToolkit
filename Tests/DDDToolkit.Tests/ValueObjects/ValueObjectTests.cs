using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Exceptions;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Tests.Domain;
using FluentAssertions;

namespace DDDToolkit.Tests.ValueObjects;

/// <summary>
/// <c>[ValueObject]</c> on a multi-property record: structural equality over the properties the author
/// chose, plus the always-valid twin. Equality covers every property except the ones marked
/// <c>[DontCompare]</c> or <c>[Internal]</c>, and computed (get-only) properties, which are never components.
/// </summary>
public class ValueObjectTests
{
    // ---------------------------------------------------------------- equality

    [Fact]
    public void EqualityIsStructural()
    {
        var left = new PersonName("John", "Doe");
        var right = new PersonName("John", "Doe");

        (left == right).Should().BeTrue();
        left.Equals(right).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void DifferentComponentsMeanDifferentValues()
    {
        var baseline = new PersonName("John", "Doe");

        (baseline == new PersonName("Johny", "Doe")).Should().BeFalse();
        (baseline == new PersonName("John", "Doh")).Should().BeFalse();
    }

    [Fact]
    public void DontComparePropertiesAreExcludedFromEquality()
    {
        // MiddleNames carries [DontCompare], so two people with the same first and last name are the
        // same PersonName however their middle names differ.
        var withoutMiddle = new PersonName("John", "Doe");
        var withMiddle = new PersonName("John", "Something", "Doe");
        var withOtherMiddle = new PersonName("John", "Else", "Doe");

        (withoutMiddle == withMiddle).Should().BeTrue();
        (withMiddle == withOtherMiddle).Should().BeTrue();
        withMiddle.GetHashCode().Should().Be(withOtherMiddle.GetHashCode(),
            "hash codes are built from the same components as equality");

        // The excluded property is still carried; it just does not identify the value.
        withMiddle.MiddleNames.Should().Be("Something");
        withMiddle.FullName.Should().Be("John Something Doe");
    }

    [Fact]
    public void DontCompareWorksForValueObjectsDeclaredInThisProject()
    {
        var left = new Address("1 Main St", "Delft", note: "ring twice");
        var right = new Address("1 Main St", "Delft", note: "leave at door");

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
        left.Note.Should().NotBe(right.Note);

        new Address("2 Main St", "Delft").Should().NotBe(left);
        new Address("1 Main St", "Rotterdam").Should().NotBe(left);
    }

    [Fact]
    public void ComputedPropertiesAreNotEqualityComponents()
    {
        // Label is get-only, so the generator never yields it. If it did, nothing would change here —
        // but it would break the moment a computed property depended on a [DontCompare] one.
        var left = new Address("1 Main St", "Delft", note: "a");
        var right = new Address("1 Main St", "Delft", note: "b");

        left.Label.Should().Be(right.Label);
        left.Should().Be(right);
    }

    [Fact]
    public void ValueObjectsWorkAsDictionaryKeys()
    {
        var byAddress = new Dictionary<Address, int>
        {
            [new Address("1 Main St", "Delft", "ring twice")] = 1,
        };

        byAddress[new Address("1 Main St", "Delft", "different note")].Should().Be(1);
        byAddress.ContainsKey(new Address("2 Main St", "Delft")).Should().BeFalse();
    }

    // ---------------------------------------------------------------- the always-valid twin

    [Fact]
    public void ToValidCopiesEverySettableProperty()
    {
        var address = new Address("1 Main St", "Delft", note: "ring twice");

        var valid = address.ToValid();

        valid.Should().BeOfType<ValidAddress>();
        valid.Should().BeAssignableTo<IAlwaysValid>();
        valid.Street.Should().Be("1 Main St");
        valid.City.Should().Be("Delft");
        valid.Note.Should().Be("ring twice", "[DontCompare] excludes a property from equality, not from the copy");
        valid.IsValidated.Should().BeTrue();
        valid.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ToValidOnAnInvalidValueThrows()
    {
        var missingStreet = new Address("", "Delft");

        var act = () => missingStreet.ToValid();

        act.Should().Throw<InvalidValueObjectException>()
            .WithMessage("*Address*");
    }

    [Fact]
    public void TheTwinKeepsStructuralEqualityWithItsOwnKind()
    {
        var left = new Address("1 Main St", "Delft", "a").ToValid();
        var right = new Address("1 Main St", "Delft", "b").ToValid();

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void TheTwinIsUsableWhereverTheValueObjectIs()
    {
        ValueObject valid = new Address("1 Main St", "Delft").ToValid();

        valid.Should().BeAssignableTo<Address>();
        valid.IsValid.Should().BeTrue();
    }

    // ---------------------------------------------------------------- 'with' and cloning

    [Fact]
    public void CloningIsRestrictedByAccessibility()
    {
        // The "cloning restriction" is a compile-time one and cannot be asserted from here: Address's
        // properties have protected init setters, so `address with { Street = "" }` does not compile
        // outside the value object. What a test can pin is that the type really is closed that way.
        foreach (var name in new[] { nameof(Address.Street), nameof(Address.City), nameof(Address.Note) })
        {
            var setter = typeof(Address).GetProperty(name)!.SetMethod;

            setter.Should().NotBeNull();
            setter!.IsPublic.Should().BeFalse($"{name} must not be settable from outside the value object");
        }
    }

    [Fact]
    public void WithProducesAnIndependentCopy()
    {
        var original = new Address("1 Main St", "Delft", "ring twice");

        var moved = original.WithCity("Rotterdam");

        moved.Should().NotBeSameAs(original);
        moved.Should().NotBe(original);
        moved.City.Should().Be("Rotterdam");
        moved.Street.Should().Be("1 Main St", "the other components ride along");
        moved.Note.Should().Be("ring twice");
        original.City.Should().Be("Delft", "the original is untouched");
    }

    [Fact]
    public void AnUnvalidatedCopyValidatesItself()
    {
        var original = new Address("1 Main St", "Delft");

        var broken = original.WithStreet("");

        broken.IsValidated.Should().BeFalse("nothing has asked for a verdict yet");
        broken.IsValid.Should().BeFalse();

        var act = () => broken.ToValid();
        act.Should().Throw<InvalidValueObjectException>();
    }

    [Fact]
    public void ACopyDoesNotInheritTheSourcesVerdict()
    {
        // Regression test for a real bug this suite found: ValueObject caches its verdict in a field,
        // and `record with` copies fields — so a copy taken from an already-validated value used to
        // report the source's verdict even when the copy changed the very property that was checked.
        // ValueObject now has an explicit copy constructor that resets the cache.
        var original = new Address("1 Main St", "Delft");
        original.IsValid.Should().BeTrue("this is what caches the verdict on the source");

        var broken = original.WithStreet("");

        broken.Street.Should().BeEmpty();
        broken.IsValidated.Should().BeFalse("the copy starts with no verdict of its own");
        broken.IsValid.Should().BeFalse("and reaches the right one when asked");

        original.IsValid.Should().BeTrue("the source is unaffected");
    }

    [Fact]
    public void ACopyOfAnInvalidValueCanBecomeValid()
    {
        // The same fix in the other direction: fixing the broken component through 'with' must be
        // enough to make the copy valid.
        var broken = new Address("", "Delft");
        broken.IsValid.Should().BeFalse();

        var fixedUp = broken.WithStreet("1 Main St");

        fixedUp.IsValid.Should().BeTrue();
        fixedUp.ToValid().Street.Should().Be("1 Main St");
    }
}
