using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.Catalog.Contracts;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Inventory.Contracts;
using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Examples.Payments.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Examples.Ordering;

/// <summary>
/// Everything Ordering needs from the host, registered by Ordering: its context, its outbox and the
/// poller that empties it, and the policies it follows. The host calls <see cref="AddOrderingModule"/>
/// and learns nothing else about how Ordering stores its orders.
/// </summary>
public static class OrderingModule
{
    /// <summary>
    /// Registers Ordering. The host says where its tables live and where what it publishes goes; see
    /// <see cref="ModuleHost"/>. Ordering lives in the <c>ordering</c> schema of whichever database that
    /// is, or in an <c>ordering.db</c> of its own on SQLite.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="host">The host's two decisions: the database, and the transport.</param>
    public static IServiceCollection AddOrderingModule(this IServiceCollection services, ModuleHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        // The context, and whatever has to happen before its first query: a SQLite file created from the
        // model, migrations applied on start-up, or the check that Supabase applied them. The Supabase
        // export does not need this line: the build finds OrderingContextFactory by its
        // [SupabaseMigrations] marker.
        host.Database.AddContext<OrderingContext, OrderingContextFactory>(services, OrderingContext.Schema);

        // Ordering produces integration events, so it owns an outbox: what it publishes, as what, and
        // that it goes wherever the host sends it. It does not know which modules listen.
        services.AddDDDToolkitEntityFramework(options => options.UseOutbox<OrderingContext>(outbox =>
        {
            outbox.RegisterEventsFromAssemblyContaining<Order>();

            // The seam. OrderPlaced is Ordering's to change; OrderPlacedV1 is what everyone deployed
            // against. The conversion runs at delivery, so the stored row stays a faithful record of what
            // happened in the domain.
            outbox.PublishAs<OrderPlaced, OrderPlacedV1>(placed => new OrderPlacedV1(
                placed.OrderId,
                placed.ShipTo.City,
                placed.ShipTo.PostalCode,
                [.. placed.Lines.Select(line => new OrderedLineV1(line.Sku, line.Quantity))],
                placed.Total.Amount,
                placed.Total.Currency));
            outbox.PublishAs<OrderConfirmed, OrderConfirmedV1>(confirmed =>
                new OrderConfirmedV1(confirmed.OrderId, confirmed.ShipTo.City, confirmed.ShipTo.PostalCode));
            outbox.PublishAs<OrderCancelled, OrderCancelledV1>(cancelled =>
                new OrderCancelledV1(cancelled.OrderId, cancelled.Reason));

            // In a monolith: the other modules in this process. In a service: a queue or a broker.
            host.Publish(outbox);

            // Sinks normally replace the in-process delegate. This asks for both: the local handler
            // (OrderLog) sees the domain events, the other modules see the contract.
            outbox.AlsoDispatchInProcess = true;
        }));

        // What Ordering reads from the others. The inbox needs the contract types to turn a delivered
        // message back into the record a handler asked for.
        services.AddDDDToolkitEntityFramework(options => options.MapIntegrationEvents(contracts => contracts
            .RegisterFromAssemblyContaining<ProductListedV1>()
            .RegisterFromAssemblyContaining<StockReservedV1>()
            .RegisterFromAssemblyContaining<PaymentSucceededV1>()));

        // Every policy in Application/IntegrationEvents/, each under Ordering's own inbox. Catalog,
        // Inventory and Payments do not know Ordering listens; they publish, and this is where Ordering
        // signs up. Whatever carries the message here, the module sink or a broker's consumer, hands it
        // to these handlers.
        services.AddModuleIntegrationEvents<OrderingContext>(module => module
            .Handle<ProductListedV1, RecordListedPrice>()
            .Handle<ProductPriceChangedV1, RecordChangedPrice>()
            .Handle<StockReservedV1, RecordStockReservation>()
            .Handle<StockReservationFailedV1, CancelWithoutStock>()
            .Handle<PaymentSucceededV1, RecordPayment>()
            .Handle<PaymentFailedV1, CancelWithoutPayment>());

        // Delivery happens on this poll, not at commit. Ordering's transaction has already committed by
        // then, which is exactly why a failing consumer cannot refuse an order.
        services.AddOutboxBackgroundService<OrderingContext>(pollingInterval: TimeSpan.FromSeconds(1));

        return services;
    }
}
