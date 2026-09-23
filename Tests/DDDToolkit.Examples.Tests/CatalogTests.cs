using DDDToolkit.Examples.Catalog;
using DDDToolkit.Examples.Ordering;
using DDDToolkit.Examples.SharedKernel;
using DDDToolkit.Testing;
using FluentAssertions;

namespace DDDToolkit.Examples.Tests;

/// <summary>Catalog's product, and Ordering's copy of its prices.</summary>
public class CatalogTests
{
    private static Product Mug() => new(ProductId.CreateSequential(), "MUG", "Mug", new Money(8m, Money.Euro).ToValid());

    [Fact]
    public void Listing_a_product_announces_it_with_its_price()
    {
        var price = Mug().PendingEvents().RaisedExactly<ProductListed>().SingleEvent<ProductListed>().Price;

        // The event holds the always-valid twin the product was listed with, so the parts are compared
        // rather than the two objects.
        (price.Amount, price.Currency).Should().Be((8m, Money.Euro));
    }

    [Fact]
    public void Repricing_announces_the_new_price_and_the_same_price_announces_nothing()
    {
        var scenario = AggregateScenario.Given(Mug());
        scenario.IgnorePendingEvents();

        scenario.When(product => product.ChangePrice(new Money(9.50m, Money.Euro).ToValid())).RaisedExactly<ProductPriceChanged>();
        scenario.When(product => product.ChangePrice(new Money(9.50m, Money.Euro).ToValid())).RaisedNothing();
    }

    [Fact]
    public void Ordering_keeps_the_newest_price_even_when_an_older_one_arrives_last()
    {
        var monday = new DateTimeOffset(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
        var price = new CatalogPrice("MUG", new Money(8m, Money.Euro), monday);

        price.Reprice(new Money(9.50m, Money.Euro), monday.AddDays(2));
        price.Reprice(new Money(8.75m, Money.Euro), monday.AddDays(1));

        price.Price.Should().Be(new Money(9.50m, Money.Euro));
    }
}
