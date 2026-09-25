using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Payments.IntegrationEvents;
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
                outbox.AddPaymentsIntegrationEvents();
                host.Publish(outbox);
            })
            .MapIntegrationEvents(contracts => contracts.AddPaymentsIntegrationEvents()));

        services.AddModuleIntegrationEvents<PaymentsContext>(module => module.AddPaymentsIntegrationEvents());

        services.AddOutboxBackgroundService<PaymentsContext>(pollingInterval: TimeSpan.FromSeconds(1));

        services.AddDomainEventRetention<PaymentsContext>(retention =>
        {
            retention.KeepOutboxFor = TimeSpan.FromDays(7);
            retention.KeepInboxFor = TimeSpan.FromDays(30);
        });

        // GraphQL, when the host serves it: Payments's own source schema, for a Fusion gateway to compose.
        if (host.GraphQL is { } graphql)
        {
            graphql(services.AddPaymentsSourceSchema());
        }

        return services;
    }
}
