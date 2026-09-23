using DDDToolkit.Examples.Inventory;
using DDDToolkit.Examples.Ordering;
using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Examples.SharedKernel;
using DDDToolkit.Testing;
using FluentAssertions;

namespace DDDToolkit.Examples.Tests;

/// <summary>
/// The two domain services. Both are pure: the prices and the stock are handed in, so the rules are
/// tested without a database.
/// </summary>
public class DomainServiceTests
{
    private static readonly IReadOnlyDictionary<string, Money> Prices = new Dictionary<string, Money>
    {
        ["COFFEE-1KG"] = new(12.50m, Money.Euro),
        ["MUG"] = new(8m, Money.Euro),
    };

    [Fact]
    public void The_pricer_prices_each_line_at_the_current_price()
    {
        var priced = OrderPricer.Price([new("COFFEE-1KG", 2), new("MUG", 1)], Prices);

        priced.UnknownSkus.Should().BeEmpty();
        priced.Lines.Select(line => line.Subtotal).Should().Equal(new Money(25m, Money.Euro), new Money(8m, Money.Euro));
    }

    [Fact]
    public void The_pricer_reports_a_SKU_it_cannot_price_instead_of_guessing()
    {
        var priced = OrderPricer.Price([new("MUG", 1), new("TEAPOT", 1)], Prices);

        priced.UnknownSkus.Should().Equal("TEAPOT");
        priced.Lines.Should().ContainSingle();
    }

    [Fact]
    public void The_pricer_leaves_a_blank_SKU_to_the_rule_that_owns_it()
    {
        var priced = OrderPricer.Price([new("  ", 1)], Prices);

        // Not reported here: OrderLine.MustNameASku is the rule about blank SKUs, and it has a code.
        priced.UnknownSkus.Should().BeEmpty();
        priced.Lines.Single().GetInvariantViolations().Select(v => v.Code).Should().Equal(OrderLine.MustNameASku.ViolationCode);
    }

    private static Dictionary<string, StockItem> Stock(int coffee, int mugs) => new()
    {
        ["COFFEE-1KG"] = new StockItem(StockItemId.CreateSequential(), "COFFEE-1KG", coffee),
        ["MUG"] = new StockItem(StockItemId.CreateSequential(), "MUG", mugs),
    };

    [Fact]
    public void The_allocator_reserves_every_line_when_every_line_can_be_had()
    {
        var stock = Stock(coffee: 10, mugs: 2);

        var reservation = StockAllocator.Allocate(OrderId.CreateSequential(), [("COFFEE-1KG", 3), ("MUG", 2)], stock);

        reservation.Status.Should().Be(ReservationStatus.Reserved);
        reservation.PendingEvents().RaisedExactly<StockReserved>();
        stock["COFFEE-1KG"].Available.Should().Be(7);
        stock["MUG"].Available.Should().Be(0);
    }

    [Fact]
    public void The_allocator_reserves_nothing_when_one_line_cannot_be_had()
    {
        var stock = Stock(coffee: 10, mugs: 2);

        var reservation = StockAllocator.Allocate(OrderId.CreateSequential(), [("COFFEE-1KG", 3), ("MUG", 3)], stock);

        // All or nothing: the coffee that was available is not set aside either.
        reservation.Status.Should().Be(ReservationStatus.Refused);
        reservation.Refusal.Should().Contain("MUG: 3 wanted, 2 available");
        reservation.PendingEvents().RaisedExactly<StockRefused>();
        stock.Values.Should().OnlyContain(item => item.Reserved == 0);
    }

    [Fact]
    public void Releasing_a_reservation_puts_the_stock_back_once()
    {
        var stock = Stock(coffee: 10, mugs: 2);
        var reservation = StockAllocator.Allocate(OrderId.CreateSequential(), [("MUG", 2)], stock);

        reservation.Release(stock);
        reservation.Release(stock);

        stock["MUG"].Available.Should().Be(2);
        reservation.Status.Should().Be(ReservationStatus.Released);
    }
}
