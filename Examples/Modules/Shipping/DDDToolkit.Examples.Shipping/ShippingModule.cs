using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Ordering.Contracts;
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

        // The payload shapes Shipping reads. The inbox needs them to turn a delivered message into the
        // record BookShipment asked for, and an upcaster from an older version would go here too.
        services.AddDDDToolkitEntityFramework(options =>
            options.MapIntegrationEvents(contracts => contracts.RegisterFromAssemblyContaining<OrderPlacedV1>()));

        // Shipping signs up as a consumer, with its own inbox. Ordering never names Shipping: it publishes,
        // and every module registered here is offered every message, whatever carried it here.
        services.AddModuleIntegrationEvents<ShippingContext>(module => module.Handle<OrderConfirmedV1, BookShipment>());

        return services;
    }
}
