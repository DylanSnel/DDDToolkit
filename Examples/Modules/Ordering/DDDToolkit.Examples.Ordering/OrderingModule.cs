using DDDToolkit.Access;
using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Ordering.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        // Who is asking, for the customer an order is placed in the name of. A host that knows its callers,
        // the Supabase monolith with signed-in customers, registers its own before this; everywhere else
        // nobody signs in, the caller is the system, and every order is a guest's.
        services.TryAddSingleton<ICallerAccessor, AmbientCallerAccessor>();

        // Ordering produces integration events, so it owns an outbox: what it publishes, as what, and
        // that it goes wherever the host sends it. It does not know which modules listen.
        services.AddDDDToolkitEntityFramework(options => options.UseOutbox<OrderingContext>(outbox =>
        {
            // Generated when the module compiles: every domain event under the name the outbox stores it
            // as, and the seam, every class in Application/<slice>/IntegrationEvents/Outbound/, one per
            // domain event that leaves. The translation lives there, next to the aggregate it publishes
            // for; this line only says that it happens. Nothing is found by reflection when it runs.
            outbox.AddOrderingIntegrationEvents();

            // In a monolith: the other modules in this process. In a service: a queue or a broker.
            host.Publish(outbox);

            // Sinks normally replace the in-process delegate. This asks for both: the local handler
            // (OrderLog) sees the domain events, the other modules see the contract.
            outbox.AlsoDispatchInProcess = true;
        }));

        // What Ordering reads from the others: the contracts its handlers take. The inbox needs them to
        // turn a delivered message back into the record a handler asked for.
        services.AddDDDToolkitEntityFramework(options => options.MapIntegrationEvents(contracts => contracts.AddOrderingIntegrationEvents()));

        // Every policy in Application/<slice>/IntegrationEvents/Inbound/, each under Ordering's own
        // inbox. Catalog, Inventory and Payments do not know Ordering listens; they publish, and this is
        // where Ordering signs up. Whatever carries the message here, the module sink or a broker's
        // consumer, hands it to these handlers.
        services.AddModuleIntegrationEvents<OrderingContext>(module => module.AddOrderingIntegrationEvents());

        // Delivery happens on this poll, not at commit. Ordering's transaction has already committed by
        // then, which is exactly why a failing consumer cannot refuse an order.
        services.AddOutboxBackgroundService<OrderingContext>(pollingInterval: TimeSpan.FromSeconds(1));

        // Neither table shrinks by itself. A delivered outbox row is history after a week. An inbox row is
        // what turns a redelivery into a repeat, so it is kept for a month, far longer than any message
        // here can take to come back.
        services.AddDomainEventRetention<OrderingContext>(retention =>
        {
            retention.KeepOutboxFor = TimeSpan.FromDays(7);
            retention.KeepInboxFor = TimeSpan.FromDays(30);
        });

        // GraphQL, when the host serves it: Ordering's own source schema, for a Fusion gateway to compose.
        if (host.GraphQL is { } graphql)
        {
            graphql(services.AddOrderingSourceSchema());
        }

        return services;
    }
}
