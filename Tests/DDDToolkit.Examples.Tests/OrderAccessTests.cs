using DDDToolkit.Abstractions.Access;
using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Examples.SharedKernel;
using FluentAssertions;

namespace DDDToolkit.Examples.Tests;

/// <summary>
/// Ordering's row access rules asked in C#, without a database: the same rules the Supabase build writes
/// as policies, so what these say is what Postgres enforces.
/// </summary>
public class OrderAccessTests
{
    private static readonly Guid Alice = Guid.Parse("a11ce000-0000-4000-8000-000000000001");

    private static readonly Guid Bob = Guid.Parse("b0b00000-0000-4000-8000-000000000002");

    private static Order PlacedBy(Guid? customer)
        => new(
            OrderId.CreateSequential(),
            new Address("Oudegracht 1", "Utrecht", "3511 AA").ToValid(),
            [new OrderLine(OrderLineId.CreateSequential(), "MUG", 1, new Money(8m, Money.Euro))],
            customer is { } id ? new CustomerId(id) : null);

    [Fact]
    public void A_customer_has_their_own_orders_and_not_somebody_elses()
    {
        var alices = PlacedBy(Alice);

        ACustomerHasTheirOrders.Allows(alices, Caller.User(Alice)).Should().BeTrue();
        ACustomerHasTheirOrders.Allows(alices, Caller.User(Bob)).Should().BeFalse();
        ACustomerHasTheirOrders.Allows(alices, Caller.Anonymous).Should().BeFalse();
    }

    [Fact]
    public void A_guests_order_is_anybodys_who_has_its_id()
    {
        var guests = PlacedBy(null);

        ACustomerHasTheirOrders.Allows(guests, Caller.Anonymous).Should().BeTrue();
        ACustomerHasTheirOrders.Allows(guests, Caller.User(Bob)).Should().BeTrue();
    }

    [Fact]
    public void An_order_is_placed_in_the_callers_own_name_or_as_a_guests_by_somebody_who_has_not_signed_in()
    {
        NobodyOrdersForSomebodyElse.Allows(PlacedBy(Alice), Caller.User(Alice)).Should().BeTrue();
        NobodyOrdersForSomebodyElse.Allows(PlacedBy(null), Caller.Anonymous).Should().BeTrue();

        NobodyOrdersForSomebodyElse.Allows(PlacedBy(Bob), Caller.User(Alice)).Should().BeFalse("nobody places an order for somebody else");
        NobodyOrdersForSomebodyElse.Allows(PlacedBy(Alice), Caller.Anonymous).Should().BeFalse();
    }

    [Fact]
    public void The_customer_of_a_caller_is_their_user_and_a_guest_has_none()
    {
        CustomerId.Of(Caller.User(Alice)).Should().Be(new CustomerId(Alice));
        CustomerId.Of(Caller.Anonymous).Should().BeNull();
        CustomerId.Of(Caller.System).Should().BeNull();
    }

    [Fact]
    public void The_rules_are_written_as_the_sql_the_policies_are_made_of()
    {
        ACustomerHasTheirOrders.RowAccessSql.Should().Be("(({col:PlacedBy} IS NULL) OR ({col:PlacedBy} IS NOT DISTINCT FROM {caller:uid}))");
        NobodyOrdersForSomebodyElse.RowAccessSql.Should().Be("({col:PlacedBy} IS NOT DISTINCT FROM {caller:uid})");
    }
}
