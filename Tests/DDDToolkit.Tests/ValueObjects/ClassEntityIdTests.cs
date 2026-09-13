using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Tests.Domain;
using FluentAssertions;

namespace DDDToolkit.Tests.ValueObjects;

/// <summary>
/// <c>[EntityId&lt;T&gt;]</c> on a <c>partial record</c> (rather than a record struct): the id derives from
/// <c>EntityId&lt;T&gt;</c>, is compared structurally, and gains an always-valid <c>Valid…</c> twin that
/// struct ids do not get.
/// </summary>
public class ClassEntityIdTests
{
    // ---------------------------------------------------------------- shape

    [Fact]
    public void ClassIdsDeriveFromEntityId()
    {
        var personId = PersonId.CreateUnique();

        typeof(PersonId).IsValueType.Should().BeFalse();
        personId.Should().BeAssignableTo<EntityId<Guid>>();
        personId.Should().BeAssignableTo<IEntityId<Guid>>();
        personId.Should().BeAssignableTo<SingleValueObject<Guid>>("EntityId<T> is a single value object");
    }

    // ---------------------------------------------------------------- equality

    [Fact]
    public void EqualityIsByValue()
    {
        var personId = PersonId.CreateUnique();
        var same = PersonId.Create(personId.Value);

        same.Should().NotBeSameAs(personId);
        (personId == same).Should().BeTrue();
        personId.Equals(same).Should().BeTrue();
        personId.GetHashCode().Should().Be(same.GetHashCode());

        (personId == PersonId.CreateUnique()).Should().BeFalse();
    }

    [Fact]
    public void IdsOfDifferentTypesNeverCompareEqual()
    {
        // Two ids can wrap the same Guid and still be different identities. This is the whole point of
        // the typed id, and it holds across the struct/class divide too.
        var catId = CatId.CreateUnique();
        var personId = PersonId.Create(catId.Value);

        personId.Value.Should().Be(catId.Value);
        catId.Equals(personId).Should().BeFalse();
        personId.Equals(catId).Should().BeFalse();
    }

    [Fact]
    public void NullIsHandled()
    {
        var personId = PersonId.CreateUnique();
        PersonId? nothing = null;

        (personId == nothing).Should().BeFalse();
        (nothing == personId).Should().BeFalse();
        personId.Equals(nothing).Should().BeFalse();
    }

    // ---------------------------------------------------------------- ToString, Parse, TryParse

    [Fact]
    public void ToStringPrefixesTheValue()
    {
        var personId = PersonId.Create(Guid.Parse("0194f0a0-1111-7000-8000-000000000001"));

        PersonId.IdPrefix.Should().Be("PRS");
        personId.ToString().Should().Be("PRS_0194f0a0-1111-7000-8000-000000000001");
    }

    [Fact]
    public void ParseRoundTripsAndThePrefixIsOptional()
    {
        var personId = PersonId.CreateUnique();

        PersonId.Parse(personId.ToString()).Should().Be(personId);
        PersonId.Parse(personId.Value.ToString()).Should().Be(personId);
        PersonId.Parse("PRS_" + personId.Value).Should().Be(personId);
    }

    [Fact]
    public void TryParseReturnsNullOnFailure()
    {
        PersonId.TryParse("not a guid", out var fromGarbage).Should().BeFalse();
        fromGarbage.Should().BeNull();

        PersonId.TryParse(null, out var fromNull).Should().BeFalse();
        fromNull.Should().BeNull();

        PersonId.TryParse("XXX_" + Guid.NewGuid(), out var fromWrongPrefix)
            .Should().BeFalse("only this id's own prefix is stripped");
        fromWrongPrefix.Should().BeNull();
    }

    [Fact]
    public void ParseThrowsOnGarbage()
    {
        var act = () => PersonId.Parse("not a guid");

        act.Should().Throw<FormatException>().WithMessage("*PersonId*");
    }

    [Fact]
    public void CreateSequentialProducesVersion7Guids()
    {
        PersonId.CreateSequential().Value.Version.Should().Be(7);
        PersonId.CreateUnique().Value.Should().NotBe(Guid.Empty);
    }

    // ---------------------------------------------------------------- the always-valid twin

    [Fact]
    public void ToValidProducesTheTwin()
    {
        var personId = PersonId.CreateUnique();

        var valid = personId.ToValid();

        valid.Should().BeOfType<ValidPersonId>();
        valid.Should().BeAssignableTo<IAlwaysValid>();
        valid.Should().BeAssignableTo<PersonId>("the twin is usable anywhere the id is");
        valid.Value.Should().Be(personId.Value);
        valid.ToString().Should().Be(personId.ToString());
    }

    [Fact]
    public void TheTwinIsAlreadyValidatedOnConstruction()
    {
        var valid = PersonId.CreateUnique().ToValid();

        valid.IsValidated.Should().BeTrue("the twin validates in its constructor so nothing revalidates later");
        valid.IsValid.Should().BeTrue();
    }

    [Fact]
    public void TheTwinCanBeBuiltStraightFromTheRawValue()
    {
        var value = Guid.NewGuid();

        var valid = new ValidPersonId(value);

        valid.Value.Should().Be(value);
        valid.Should().Be(new ValidPersonId(value));
        valid.GetHashCode().Should().Be(new ValidPersonId(value).GetHashCode());
    }

    [Fact]
    public void StructIdsHaveNoTwinByDesign()
    {
        // A struct cannot inherit, so there is no place to hang a Valid… twin. Ids that need a
        // validated form must be declared as a record class.
        typeof(CatId).GetMethod("ToValid").Should().BeNull();
        typeof(PersonId).GetMethod("ToValid").Should().NotBeNull();
    }

    // ---------------------------------------------------------------- ids declared by the test project

    [Fact]
    public void LocallyDeclaredClassIdsBehaveTheSame()
    {
        var value = Guid.NewGuid();
        var ledgerId = LedgerId.Create(value);

        ledgerId.ToString().Should().Be("LDG_" + value);
        LedgerId.Parse(ledgerId.ToString()).Should().Be(ledgerId);
        LedgerId.TryParse("rubbish", out _).Should().BeFalse();
        ledgerId.ToValid().Value.Should().Be(value);
    }
}
