using Examples.Tenancy.Tenants.Application.History;
using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations;
using Examples.Tenancy.Tenants.Domain.Aggregates.Seats;
using DDDToolkit.Localization;
using DDDToolkit.Supporting.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tenants.Application;

/// <summary>
/// Registers what this project adds to the host: the check every one of its commands and queries passes before
/// its handler, and the texts of Tenancy's refusals in English and Dutch. The module's entry,
/// <c>AddTenantsModule</c> in the API project, calls it next to the infrastructure project's registration,
/// so the host still makes one call per module.
/// </summary>
public static class TenantsApplicationServices
{
    /// <summary>
    /// Registers <see cref="TenantsAccessBehavior{TMessage, TResponse}"/> in the pipeline of this module's
    /// requests, per scope like the handlers it runs before, with the set of checks it asks; and, with the
    /// toolkit's localizer, the package's texts (<see cref="TenancyFailures"/>), those of the rules the
    /// application adds to a seat and to a unit, and what reading the access history refuses with.
    /// </summary>
    /// <remarks>
    /// The behavior is the one the toolkit's generator writes for <see cref="ITenantsRequest"/>, and
    /// <c>AddTenantsAccessBehavior</c> is written with it. Every case a request of this module declares is the
    /// Tenancy package's, so the module writes no check of its own: the package's is added for the interface by
    /// the infrastructure project, over the module's context, with <c>AddTenancyAccess</c>.
    /// <para>
    /// The handlers themselves are not registered here. The host's mediator finds them in this assembly when the
    /// host compiles and registers them itself, so a request without a handler is a build error there, not a
    /// missing line here. What they take is registered elsewhere: the package's use cases, answers and catalogue
    /// by the generated <c>AddTenancy&lt;TContext&gt;()</c>, and the port by the infrastructure project.
    /// </para>
    /// <para>
    /// Behaviors run in the order they were registered. The host registers its own first, so this one runs
    /// inside them and right before the handler. Registered once however often the module is added.
    /// </para>
    /// <para>
    /// The texts are added here, with the module, and not by the host: a host that adds the module answers its
    /// refusals in the language a request asks for without naming a resource file of the module's. The localizer
    /// asks its sources in the order they were added, so a host that wants another tone, or another word for a
    /// tenant or a seat, adds a resource file of its own with those codes, in every language it supports, before
    /// it adds the module. Reading resource files needs <c>AddLocalization()</c>, which is the host's to call.
    /// </para>
    /// </remarks>
    /// <param name="services">The host's services.</param>
    public static IServiceCollection AddTenantsApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTenantsAccessBehavior();

        // What Tenancy refuses with, in English and Dutch: the package's texts, then the application's own rules'.
        services.AddDDDToolkitLocalization(texts => texts
            .AddResource<TenancyFailures>()
            .AddResource<SeatFailures>()
            .AddResource<OrganizationFailures>()
            .AddResource<HistoryFailures>());

        return services;
    }
}
