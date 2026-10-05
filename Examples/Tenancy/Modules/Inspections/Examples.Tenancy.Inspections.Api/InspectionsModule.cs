using Examples.Tenancy.Inspections.Api.GraphQL;
using Examples.Tenancy.Inspections.Api.Operators.Rest;
using Examples.Tenancy.Inspections.Api.Recording.Rest;
using Examples.Tenancy.Inspections.Application;
using Examples.Tenancy.Inspections.Domain.Aggregates.Inspections.ValueObjects;
using Examples.Tenancy.Inspections.Infrastructure;
using Examples.Hosting;
using DDDToolkit.HotChocolate.Paging;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Inspections.Api;

/// <summary>
/// The Inspections module, as a host adds it: one call that registers it, and one that maps its routes.
/// </summary>
/// <remarks>
/// The module's entry, and the one public type of this project. It lives here because a host should reference one
/// project of a module: the API project is what a host serves, so it composes the rest, the infrastructure
/// project's registration and the application project's. It is the only type in this project that names the
/// infrastructure project, and it only composes: it holds nothing, decides nothing, and no route is written here.
/// <para>
/// The routes are written per feature, in a folder named as the feature is in the application project; there is
/// one, <c>Recording</c>, with its routes in <c>Rest</c>. It lives in the module, so every host that runs the
/// module serves the same API. A route decides nothing, and takes nothing but the sender: it makes a command or a
/// query of its arguments and sends it. On its way to its handler the request passes the module's access check,
/// which asks Projects' gate and Tenancy who is calling and refuses with a coded
/// <see cref="Exceptions.RefusalException"/>; the host turns that into problem+json. The host also decides what
/// the group the routes are mapped into requires, a seat in the tenant the request names; the module never names
/// the host's filters.
/// </para>
/// </remarks>
public static class InspectionsModule
{
    /// <summary>
    /// Registers Inspections: how it is stored and its use cases. Its key is not registered here: the host adds it
    /// with every module's, from the list the module marks with <c>[TenancyPermissions]</c>. The host says where
    /// its tables live and where what it publishes goes (see <see cref="ModuleHost"/>). Register it after Tenancy
    /// and Projects: Inspections asks Tenancy's answers and Projects' gate, and its context checks its saves with
    /// Tenancy's interceptor.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="host">The host's two decisions: the database, and the transport.</param>
    public static IServiceCollection AddInspectionsModule(this IServiceCollection services, ModuleHost host)
    {
        services.AddInspectionsInfrastructure(host).AddInspectionsApplication();

        // The lists are ordered by the inspection's id as their last key, and the paging library writes the keys
        // into its cursors. The schema's generated bindings tell it how to write an id; a host that serves no
        // GraphQL has no schema, and its route pages all the same, so the id is registered here: once, however
        // often it is asked. Here, and not beside the ordering in the infrastructure project, because what writes
        // an id into a cursor is the toolkit's GraphQL package's, which the infrastructure project does not know.
        SingleValueCursorKeySerializer<InspectionId, Guid>.Register();

        // The module's GraphQL source schema, when the host serves GraphQL. No context is registered with it: a
        // field only sends, and every query that reads takes a context of its own from the pool.
        services.AddInspectionsGraphQL(host);
        return services;
    }

    /// <summary>
    /// Maps every Inspections route, each for a seat in the tenant the request selected:
    /// <c>GET /projects/{id}/inspections</c> and <c>POST /projects/{id}/inspections</c>. The module never names
    /// the host's filters: it maps into the group it is given.
    /// </summary>
    /// <param name="seated">The group for callers with a seat. What it requires is the host's to decide.</param>
    public static IEndpointRouteBuilder MapInspectionsModule(this IEndpointRouteBuilder seated)
        => seated.MapRecordingEndpoints();

    /// <summary>
    /// Maps what the application's own staff read of the inspections:
    /// <c>GET /operations/tenants/{tenant}/projects/{project}/inspections</c>, every inspection of one project. An
    /// operator holds no seat, so the host maps it outside the group that requires one.
    /// </summary>
    /// <param name="operators">The group for operators. What it requires is the host's to decide.</param>
    public static IEndpointRouteBuilder MapInspectionsOperations(this IEndpointRouteBuilder operators)
        => operators.MapOperatorsEndpoints();
}
