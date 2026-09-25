using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Shipping.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Examples.Shipping;

/// <summary>
/// Everything Shipping needs from the host, registered by Shipping: its context, the contracts it reads,
/// and the integration events it handles under its own inbox.
/// </summary>
public static class ShippingModule
{
    /// <summary>
    /// Registers Shipping. It lives in the <c>shipping</c> schema of the database the host chose, or in a
    /// <c>shipping.db</c> on SQLite. It publishes nothing, so the host's transport only matters to it on
    /// the way in.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="host">The host's two decisions: the database, and the transport.</param>
    public static IServiceCollection AddShippingModule(this IServiceCollection services, ModuleHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        host.Database.AddContext<ShippingContext, ShippingContextFactory>(services, ShippingContext.Schema);

        // The payload shapes Shipping reads, as the compiler found them on its handlers. The inbox needs
        // them to turn a delivered message into the record BookShipment asked for; an upcaster from an
        // older version would go here too.
        services.AddDDDToolkitEntityFramework(options =>
            options.MapIntegrationEvents(contracts => contracts.AddShippingIntegrationEvents()));

        // Shipping signs up as a consumer, with its own inbox: every handler in Application/<slice>/
        // IntegrationEvents/Inbound/, found when the module compiled. Ordering never names Shipping: it
        // publishes, and every module registered here is offered every message, whatever carried it here.
        services.AddModuleIntegrationEvents<ShippingContext>(module => module.AddShippingIntegrationEvents());

        // Shipping has an inbox and no outbox, so only the inbox has a window.
        services.AddDomainEventRetention<ShippingContext>(retention => retention.KeepInboxFor = TimeSpan.FromDays(30));

        // GraphQL, when the host serves it: Shipping's own source schema, for a Fusion gateway to compose.
        if (host.GraphQL is { } graphql)
        {
            graphql(services.AddShippingSourceSchema());
        }

        return services;
    }
}
