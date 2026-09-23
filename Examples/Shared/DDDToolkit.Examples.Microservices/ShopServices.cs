using DDDToolkit.Examples.Catalog;
using DDDToolkit.Examples.Catalog.Api;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Inventory;
using DDDToolkit.Examples.Inventory.Api;
using DDDToolkit.Examples.Ordering;
using DDDToolkit.Examples.Ordering.Api;
using DDDToolkit.Examples.Payments;
using DDDToolkit.Examples.Payments.Api;
using DDDToolkit.Examples.Shipping;
using DDDToolkit.Examples.Shipping.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Examples.Microservices;

/// <summary>The three deployables the shop is cut into.</summary>
public enum ShopService
{
    /// <summary>Catalog and Ordering: what a customer browses and buys.</summary>
    Storefront,

    /// <summary>Payments on its own: the one that talks to the payment provider.</summary>
    Payments,

    /// <summary>Inventory and Shipping: the warehouse.</summary>
    Fulfilment,
}

/// <summary>
/// The shop as three services over the same five modules the monoliths run. Where a module lives is a
/// deployment decision, and this is the one place it is made.
/// </summary>
/// <remarks>
/// <b>Services are not modules.</b> Storefront runs Catalog and Ordering in one process, so a price Catalog
/// publishes reaches Ordering through the module sink, next door, exactly as in the monolith. Only a
/// message another service consumes leaves the process, and only to the services that consume it:
/// <see cref="ConsumersOf"/> is that routing table, and the transport of each sample reads it.
/// </remarks>
public static class ShopServices
{
    /// <summary>The name of a service: its Aspire resource, its queue, its endpoint.</summary>
    public static string NameOf(ShopService service) => service.ToString().ToLowerInvariant();

    /// <summary>
    /// Which other services consume a published contract, by its published name. A service is never
    /// among the consumers of what it publishes itself: its own modules hear it through the module sink.
    /// </summary>
    /// <remarks>
    /// A table somebody wrote, rather than something worked out from the handlers, on purpose: it is the
    /// deployment's contract between services, and a message nobody routes is a message nobody receives,
    /// which is exactly what a reviewer should see change in a diff.
    /// </remarks>
    public static IReadOnlyList<ShopService> ConsumersOf(string contract)
        => Routing.TryGetValue(contract, out var consumers) ? consumers : [];

    /// <summary>
    /// The contracts <paramref name="service"/> has to be sent: what its queue is bound to, on a broker
    /// that routes by key.
    /// </summary>
    public static IEnumerable<string> ContractsFor(ShopService service)
        => Routing.Where(route => route.Value.Contains(service)).Select(route => route.Key);

    /// <summary>Every contract that crosses from one service to another, and who it goes to.</summary>
    /// <remarks>
    /// Catalog's prices are not here: Ordering reads them, and it runs next to Catalog in Storefront.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, ShopService[]> Routing = new Dictionary<string, ShopService[]>
    {
        ["ordering.order-placed"] = [ShopService.Payments, ShopService.Fulfilment],
        ["ordering.order-cancelled"] = [ShopService.Payments, ShopService.Fulfilment],
        ["ordering.order-confirmed"] = [ShopService.Fulfilment],
        ["inventory.stock-reserved"] = [ShopService.Storefront, ShopService.Payments],
        ["inventory.stock-reservation-failed"] = [ShopService.Storefront],
        ["payments.payment-succeeded"] = [ShopService.Storefront],
        ["payments.payment-failed"] = [ShopService.Storefront],
    };

    /// <summary>The services <paramref name="from"/> sends <paramref name="contract"/> to.</summary>
    public static IEnumerable<ShopService> RecipientsOf(string contract, ShopService from)
        => ConsumersOf(contract).Where(service => service != from);

    /// <summary>Registers the modules <paramref name="service"/> runs.</summary>
    public static IServiceCollection AddShopService(this IServiceCollection services, ShopService service, ModuleHost host)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);

        switch (service)
        {
            case ShopService.Storefront:
                services.AddCatalogModule(host);
                services.AddOrderingModule(host);
                break;
            case ShopService.Payments:
                services.AddPaymentsModule(host);
                break;
            case ShopService.Fulfilment:
                services.AddInventoryModule(host);
                services.AddShippingModule(host);
                break;
        }

        return services;
    }

    /// <summary>Maps the REST endpoints of the modules <paramref name="service"/> runs.</summary>
    public static WebApplication MapShopService(this WebApplication app, ShopService service)
    {
        ArgumentNullException.ThrowIfNull(app);

        switch (service)
        {
            case ShopService.Storefront:
                app.MapCatalogEndpoints();
                app.MapOrderingEndpoints();
                break;
            case ShopService.Payments:
                app.MapPaymentsEndpoints();
                break;
            case ShopService.Fulfilment:
                app.MapInventoryEndpoints();
                app.MapShippingEndpoints();
                break;
        }

        return app;
    }

    /// <summary>
    /// The REST paths each service serves, for the gateway in front of them: a client sees one shop at
    /// one address, whichever service answers.
    /// </summary>
    public static IReadOnlyDictionary<string, ShopService> Routes { get; } = new Dictionary<string, ShopService>
    {
        ["/products"] = ShopService.Storefront,
        ["/orders"] = ShopService.Storefront,
        ["/payments"] = ShopService.Payments,
        ["/stock"] = ShopService.Fulfilment,
        ["/reservations"] = ShopService.Fulfilment,
        ["/shipments"] = ShopService.Fulfilment,
    };
}
