using DDDToolkit.EntityFramework;
using Examples.Tenancy.Inspections.Application.Access;
using Examples.Tenancy.Inspections.Infrastructure.IntegrationEvents;
using Examples.Hosting;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Inspections.Infrastructure;

/// <summary>
/// Everything of Inspections that knows how it is stored, registered by Inspections: its context, the adapters over
/// it that implement the application project's two ports, its outbox and the poller that empties it.
/// </summary>
/// <remarks>
/// Half of the module's composition. The other half is the application project's own registration, and the
/// module's entry, <c>AddInspectionsModule</c> in the API project, calls both. That entry is the only code
/// outside this project that names it: the host references the API project alone, and nothing else in the API
/// project knows this one exists.
/// </remarks>
public static class InspectionsInfrastructure
{
    /// <summary>
    /// Registers how Inspections is stored. The host says where its tables live and where what it publishes goes
    /// (see <see cref="ModuleHost"/>). Tenancy must be registered as well, by the Tenants module: Inspections'
    /// context checks its saves with Tenancy's interceptor, which <c>AddTenancy</c> brings to every context, and its
    /// model, which keeps inspections to a tenant, has its first save refused where nothing brought it. The module
    /// runs on Postgres and on nothing else, so the host is one built on its connections
    /// (<see cref="ModuleHost.OnPostgres"/>).
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="host">The host's two decisions: the database, and the transport.</param>
    /// <exception cref="InvalidOperationException">The host was not built on Postgres.</exception>
    public static IServiceCollection AddInspectionsInfrastructure(this IServiceCollection services, ModuleHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        // The context, from a pool: the reads, which each take a context of their own, and the request's own
        // context both draw on it. The provider is the pools' to set, on the host's connections for requests or for
        // the background. The factory is the one dotnet ef and the export build the context with: its migrations are
        // what the host is checked against.
        //
        // One call wires it, each lock in its place: the toolkit's interceptors, then what the host's registrations
        // bring. Row level security, which the host registered, puts the caller's role and claims on every
        // connection, so the exported policies see who asks. Tenancy's save check, which AddTenancy brought, comes
        // after the toolkit's interceptors, so it sees what they let through, and refuses an inspection of another
        // tenant before anything is written.
        host.RequirePostgres().AddContext<InspectionsContext, InspectionsContextFactory>(services, InspectionsContext.Schema, (application, options) => options
            .UseDDDToolkit(application));

        // The application's two ports. What a command records goes through the request's context, its unit of
        // work, taken from the pool when the request first asks for it; what a query reads takes a context of its
        // own from the pool's factory, since the queries of one request may run side by side.
        services.AddScoped<IInspectionStore, EfInspectionStore>();
        services.AddScoped<IInspectionReads, EfInspectionReads>();

        // One thing a request of this module requires is Tenancy's to decide: that the caller is an operator. The
        // package's check for Tenancy's cases, added for this module's request interface and its context.
        // Inspections declares none of Tenancy's classes, so the application's four ids are written out. No request
        // of the module asks a key of the tenant's, which is the one case the check would read this context for.
        services.AddTenancyAccess<TenantId, SeatId, OrganizationUnitId, RoleId, IInspectionsRequest, InspectionsContext>();

        // Inspections' domain events are stored with the inspection, and delivered wherever the host sends what
        // modules publish. Generated when the module compiles: every domain event of the domain project, under the
        // name its module gives it, inspections.inspection-recorded.
        services.AddDDDToolkitEntityFramework(options => options.UseOutbox<InspectionsContext>(outbox =>
        {
            outbox.AddInspectionsIntegrationEvents();
            host.Publish(outbox);
        }));

        // The poller marks each stored event handled. It needs no Tenancy caller: the outbox is not kept to a tenant.
        services.AddOutboxBackgroundService<InspectionsContext>(pollingInterval: TimeSpan.FromSeconds(1));

        return services;
    }
}
