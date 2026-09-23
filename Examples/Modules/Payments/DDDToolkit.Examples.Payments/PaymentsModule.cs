using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Inventory.Contracts;
using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Examples.Payments.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DDDToolkit.Examples.Payments;

/// <summary>Everything Payments needs from the host, registered by Payments.</summary>
public static class PaymentsModule
{
    /// <summary>
    /// Registers Payments: its context, its outbox, its three policies and the payment provider. A host
    /// that registers its own <see cref="IPaymentProvider"/> first keeps it; the fake is the default.
    /// </summary>
    public static IServiceCollection AddPaymentsModule(this IServiceCollection services, ModuleHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        host.Database.AddContext<PaymentsContext, PaymentsContextFactory>(services, PaymentsContext.Schema);

        // A singleton, because the fake remembers what it answered per idempotency key, as a real
        // provider does on its side.
        services.TryAddSingleton<IPaymentProvider, FakePaymentProvider>();

        services.AddDDDToolkitEntityFramework(options => options
            .UseOutbox<PaymentsContext>(outbox =>
            {
                outbox.RegisterEventsFromAssemblyContaining<Payment>();
                outbox.PublishAs<PaymentCaptured, PaymentSucceededV1>(captured =>
                    new PaymentSucceededV1(captured.OrderId, captured.Amount.Amount, captured.Amount.Currency));
                outbox.PublishAs<PaymentDeclined, PaymentFailedV1>(declined => new PaymentFailedV1(declined.OrderId, declined.Reason));
                host.Publish(outbox);
            })
            .MapIntegrationEvents(contracts => contracts
                .RegisterFromAssemblyContaining<OrderPlacedV1>()
                .RegisterFromAssemblyContaining<StockReservedV1>()));

        services.AddModuleIntegrationEvents<PaymentsContext>(module => module
            .Handle<OrderPlacedV1, OpenPayment>()
            .Handle<StockReservedV1, TakePayment>()
            .Handle<OrderCancelledV1, VoidPayment>());

        services.AddOutboxBackgroundService<PaymentsContext>(pollingInterval: TimeSpan.FromSeconds(1));

        return services;
    }
}
