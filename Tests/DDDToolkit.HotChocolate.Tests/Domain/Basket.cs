using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.HotChocolate.Tests.Domain;

/// <summary>An id for <see cref="Basket"/>.</summary>
[EntityId<Guid>]
public readonly partial record struct BasketId
{
    /// <summary>An id over the given value.</summary>
    public static BasketId Create(Guid value) => new(value);
}

/// <summary>
/// An aggregate with behaviour that changes its state and returns something: exactly what an implicitly
/// bound schema must never publish, because the field would run it inside a query.
/// </summary>
[AggregateRoot<BasketId>]
public partial class Basket
{
    /// <summary>A basket holding <paramref name="total"/>.</summary>
    public Basket(BasketId id, Price total) : base(id) => Total = total;

    /// <summary>What is in it.</summary>
    public Price Total { get; private set; }

    /// <summary>How often it was checked out.</summary>
    public int CheckedOut { get; private set; }

    /// <summary>Checks the basket out, and says how often it has been.</summary>
    public int Checkout() => ++CheckedOut;
}

/// <summary>A value object with a computed property, which is data, and a method, which is behaviour.</summary>
[ValueObject]
public partial record Price(decimal Amount, string Currency)
{
    /// <summary>How the price reads.</summary>
    public string Display => $"{Amount:0.00} {Currency}";

    /// <summary>This price, <paramref name="quantity"/> times.</summary>
    public Price Times(int quantity) => With(amount: Amount * quantity);
}
