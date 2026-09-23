using DDDToolkit.Examples.SharedKernel;

namespace DDDToolkit.Examples.Ordering.Application.ReadModels;

/// <summary>
/// What Ordering last heard a SKU costs. A read model, not an aggregate: Catalog decides prices, and this
/// is Ordering's copy of the decision, kept current by <c>Application/IntegrationEvents/PriceList.cs</c>.
/// </summary>
/// <remarks>
/// A copy rather than a question to Catalog at checkout, because a module that has to ask another one
/// before it can take an order cannot take orders while the other is down. The price of that is that the
/// copy can be a second behind. For a shop that is an acceptable answer; a bank would choose differently.
/// <para>
/// A plain class with no <c>[AggregateRoot]</c>: nothing raises events from it, nothing guards a rule on
/// it, and its only writer is the handler. It is stored in Ordering's context like any other table, keyed
/// on the SKU.
/// </para>
/// </remarks>
public sealed class CatalogPrice
{
    public CatalogPrice(string sku, Money price, DateTimeOffset pricedAt)
    {
        Sku = sku;
        Price = price;
        PricedAt = pricedAt;
    }

    // For Entity Framework, which materialises the row without the constructor above.
    private CatalogPrice()
    {
        Price = Money.Zero();
    }

    public string Sku { get; private set; } = string.Empty;

    public Money Price { get; private set; }

    /// <summary>When Catalog set this price, taken from the message, not from when it arrived.</summary>
    public DateTimeOffset PricedAt { get; private set; }

    /// <summary>
    /// Takes a price Catalog set at <paramref name="pricedAt"/>, unless it is older than the one held.
    /// Messages from one producer can still arrive out of order once a broker is involved, and a stale
    /// price must not overwrite a newer one just because it was delivered later.
    /// </summary>
    public void Reprice(Money price, DateTimeOffset pricedAt)
    {
        if (pricedAt < PricedAt)
        {
            return;
        }

        Price = price;
        PricedAt = pricedAt;
    }
}
