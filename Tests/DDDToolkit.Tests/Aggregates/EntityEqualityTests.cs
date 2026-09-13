using System.Reflection;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Tests.Domain;
using FluentAssertions;

namespace DDDToolkit.Tests.Aggregates;

/// <summary>
/// Pins <c>Entity&lt;TId&gt;</c>'s equality contract for both id kinds. The headline is
/// <see cref="EntitiesWithTheSameIdTypeAreEqualAcrossTypes"/>: equality is by id alone, never by runtime
/// type, so two different aggregate types sharing an id type compare equal.
/// </summary>
public class EntityEqualityTests
{
    // ---------------------------------------------------------------- struct ids

    [Fact]
    public void SameStructIdMeansEqual()
    {
        var id = BasketId.CreateUnique();
        var left = new Basket(id, "ada");
        var right = new Basket(id, "grace");

        left.Equals(right).Should().BeTrue("equality is by identity, not by state");
        (left == right).Should().BeTrue();
        (left != right).Should().BeFalse();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void DifferentStructIdMeansNotEqual()
    {
        var left = new Basket(BasketId.CreateUnique(), "ada");
        var right = new Basket(BasketId.CreateUnique(), "ada");

        left.Equals(right).Should().BeFalse();
        (left == right).Should().BeFalse();
        (left != right).Should().BeTrue();
    }

    [Fact]
    public void ReferenceEqualIsAlwaysEqual()
    {
        var basket = new Basket(BasketId.CreateUnique(), "ada");

        basket.Equals(basket).Should().BeTrue();
#pragma warning disable CS1718 // Comparison to same variable is the point of the test.
        (basket == basket).Should().BeTrue();
#pragma warning restore CS1718
    }

    // ---------------------------------------------------------------- class ids

    [Fact]
    public void SameClassIdMeansEqual()
    {
        var value = Guid.NewGuid();
        var left = new Ledger(LedgerId.Create(value));
        var right = new Ledger(LedgerId.Create(value));

        left.Id.Should().NotBeSameAs(right.Id, "the ids are distinct instances");
        left.Equals(right).Should().BeTrue();
        (left == right).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void DifferentClassIdMeansNotEqual()
    {
        var left = new Ledger(LedgerId.CreateUnique());
        var right = new Ledger(LedgerId.CreateUnique());

        left.Equals(right).Should().BeFalse();
        (left != right).Should().BeTrue();
    }

    // ---------------------------------------------------------------- null handling

    [Fact]
    public void OperatorsAreNullSafe()
    {
        var basket = new Basket(BasketId.CreateUnique(), "ada");
        Basket? nothing = null;
        Basket? alsoNothing = null;

        (nothing == alsoNothing).Should().BeTrue();
        (nothing != alsoNothing).Should().BeFalse();
        (basket == nothing).Should().BeFalse();
        (nothing == basket).Should().BeFalse();
        (basket != nothing).Should().BeTrue();
        (nothing != basket).Should().BeTrue();
    }

    [Fact]
    public void EqualsRejectsForeignObjects()
    {
        object basket = new Basket(BasketId.CreateUnique(), "ada");
        object foreign = "not an entity";
        object otherEntity = new Ledger(LedgerId.CreateUnique());

        basket.Equals(foreign).Should().BeFalse("a string is not an entity");
        basket.Equals(otherEntity).Should().BeFalse("a Ledger is not an Entity<BasketId>");
    }

    [Fact]
    public void EqualsRejectsNull()
    {
        // Keep these last in their own test: the compiler's nullable analysis learns from
        // `x.Equals(null)` that x might be null, and warns on anything that dereferences x afterwards.
        var basket = new Basket(BasketId.CreateUnique(), "ada");
        object asObject = basket;

        basket.Equals(null).Should().BeFalse("the typed overload rejects null");
        asObject.Equals(null).Should().BeFalse("so does the object overload");
    }

    [Fact]
    public void GetHashCodeIsZeroWhenTheIdWasNeverAssigned()
    {
        // The generated protected parameterless constructor is what EF Core and serializers use; it
        // leaves a reference-type Id null until the framework sets it. GetHashCode must survive that.
        var ledger = (Ledger)Activator.CreateInstance(typeof(Ledger), nonPublic: true)!;

        ledger.Id.Should().BeNull();
        ledger.GetHashCode().Should().Be(0);
        ledger.Equals(new Ledger(LedgerId.CreateUnique())).Should().BeFalse();
    }

    // ---------------------------------------------------------------- the sharp edge

    [Fact]
    public void EntitiesWithTheSameIdTypeAreEqualAcrossTypes()
    {
        // DECISION (pinned, not a bug report): Entity<TId>.Equals compares ids only. The type test at
        // the top of Equals is `obj is Entity<TIdObject>`, which is open over the *declaring* type, so
        // any two entities keyed by the same id type compare equal when their ids match — including an
        // aggregate root and a child entity, and two unrelated roots.
        //
        // The id type is therefore the unit of separation: give each aggregate its own id type (which
        // is what [EntityId<T>] makes cheap) and the situation below cannot arise in practice.
        var id = BasketId.CreateUnique();
        var basket = new Basket(id, "ada");
        var probe = new EventProbe(id);
        var snapshot = new BasketSnapshot(id);

        basket.Equals(probe).Should().BeTrue("two roots over BasketId are compared by id alone");
        basket.Equals(snapshot).Should().BeTrue("a root and a child entity over BasketId are too");
        probe.Equals(snapshot).Should().BeTrue();
        basket.GetHashCode().Should().Be(snapshot.GetHashCode());

        // What does distinguish them is the static type: they are not assignable to each other, and
        // the roots carry IAggregateRoot / IHasDomainEvents while the child entity does not.
        basket.Should().NotBeOfType<BasketSnapshot>();
        snapshot.Should().NotBeAssignableTo<IAggregateRoot>();
        basket.Should().BeAssignableTo<IAggregateRoot>();
    }

    [Fact]
    public void EntityIsGenericOverTheIdType()
    {
        // The closed generic base is what makes the comparison above possible, and what stops a
        // Ledger (Entity<LedgerId>) from ever equalling a Basket (Entity<BasketId>).
        typeof(Basket).Should().BeAssignableTo<Entity<BasketId>>();
        typeof(BasketSnapshot).Should().BeAssignableTo<Entity<BasketId>>();
        typeof(Ledger).Should().NotBeAssignableTo<Entity<BasketId>>();
    }

    [Fact]
    public void IdSetterIsNotPublic()
    {
        // Nothing outside the aggregate may re-key an entity.
        var setter = typeof(Basket).GetProperty("Id")!.SetMethod;

        setter.Should().NotBeNull();
        setter!.IsPublic.Should().BeFalse();
        setter.IsFamily.Should().BeTrue("Entity<TId>.Id has a protected setter");
    }

    [Fact]
    public void GeneratedParameterlessConstructorIsProtected()
    {
        var constructor = typeof(Basket).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);

        constructor.Should().NotBeNull();
        constructor!.IsFamily.Should().BeTrue();
        typeof(Basket).GetConstructor(Type.EmptyTypes).Should().BeNull("it must not be public");
    }
}
