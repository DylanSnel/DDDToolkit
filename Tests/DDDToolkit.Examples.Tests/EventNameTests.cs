using DDDToolkit.BaseTypes;
using DDDToolkit.Examples.Catalog.Contracts;
using DDDToolkit.Examples.Catalog.Domain.Products;
using DDDToolkit.Examples.Inventory.Contracts;
using DDDToolkit.Examples.Inventory.Domain.StockReservations;
using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Examples.Ordering.Domain.Orders;
using DDDToolkit.Examples.Payments.Contracts;
using DDDToolkit.Examples.Payments.Domain.Payments;
using FluentAssertions;

namespace DDDToolkit.Examples.Tests;

/// <summary>
/// The shop's events used to spell out every name in <c>[DomainEventName]</c> and <c>[IntegrationEvent]</c>,
/// and every one of those names was the module and the class name in kebab case. The attributes are gone
/// and the convention writes the same names; these pin that nothing on the wire or in an outbox moved.
/// Each name is checked three ways: the literal it always was, the runtime's reflection, and the constant
/// the generator wrote when the module compiled.
/// </summary>
public sealed class EventNameTests
{
    public static TheoryData<Type, string, int, string> DomainEvents => new()
    {
        { typeof(OrderPlaced), "ordering.order-placed", 1, Ordering.OrderingEventNames.OrderPlaced },
        { typeof(OrderConfirmed), "ordering.order-confirmed", 1, Ordering.OrderingEventNames.OrderConfirmed },
        { typeof(OrderCancelled), "ordering.order-cancelled", 1, Ordering.OrderingEventNames.OrderCancelled },
        { typeof(ProductListed), "catalog.product-listed", 1, Catalog.CatalogEventNames.ProductListed },
        { typeof(ProductPriceChanged), "catalog.product-price-changed", 1, Catalog.CatalogEventNames.ProductPriceChanged },
        { typeof(StockReserved), "inventory.stock-reserved", 1, Inventory.InventoryEventNames.StockReserved },
        { typeof(StockRefused), "inventory.stock-refused", 1, Inventory.InventoryEventNames.StockRefused },
        { typeof(PaymentCaptured), "payments.payment-captured", 1, Payments.PaymentsEventNames.PaymentCaptured },
        { typeof(PaymentDeclined), "payments.payment-declined", 1, Payments.PaymentsEventNames.PaymentDeclined },
    };

    public static TheoryData<Type, string, int, string> Contracts => new()
    {
        { typeof(OrderPlacedV1), "ordering.order-placed", 1, Ordering.Contracts.OrderingEventNames.OrderPlaced },
        { typeof(OrderConfirmedV1), "ordering.order-confirmed", 1, Ordering.Contracts.OrderingEventNames.OrderConfirmed },
        { typeof(OrderCancelledV1), "ordering.order-cancelled", 1, Ordering.Contracts.OrderingEventNames.OrderCancelled },
        { typeof(ProductListedV1), "catalog.product-listed", 1, Catalog.Contracts.CatalogEventNames.ProductListed },
        { typeof(ProductPriceChangedV1), "catalog.product-price-changed", 1, Catalog.Contracts.CatalogEventNames.ProductPriceChanged },
        { typeof(StockReservedV1), "inventory.stock-reserved", 1, Inventory.Contracts.InventoryEventNames.StockReserved },
        { typeof(StockReservationFailedV1), "inventory.stock-reservation-failed", 1, Inventory.Contracts.InventoryEventNames.StockReservationFailed },
        { typeof(PaymentSucceededV1), "payments.payment-succeeded", 1, Payments.Contracts.PaymentsEventNames.PaymentSucceeded },
        { typeof(PaymentFailedV1), "payments.payment-failed", 1, Payments.Contracts.PaymentsEventNames.PaymentFailed },
    };

    [Theory]
    [MemberData(nameof(DomainEvents))]
    public void Every_domain_event_is_stored_under_the_name_it_always_had(Type type, string name, int version, string constant)
    {
        DomainEventName.Of(type).Should().Be(name);
        IntegrationEventContract.VersionOf(type).Should().Be(version);
        constant.Should().Be(name, "the generator and the runtime spell it alike");
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void Every_contract_is_published_under_the_name_and_version_it_always_had(Type type, string name, int version, string constant)
    {
        IntegrationEventContract.NameOf(type).Should().Be(name);
        IntegrationEventContract.VersionOf(type).Should().Be(version, "the V1 its class name ends in");
        constant.Should().Be(name, "the generator and the runtime spell it alike");
        IntegrationEventContract.EntityNameOf(type).Should().Be(name + ".v" + version, "the exchange a broker gives it");
    }
}
