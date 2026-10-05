using Examples.Tenancy.Projects.Api.Access.Rest;
using Examples.Tenancy.Projects.Api.Crew.Rest;
using Examples.Tenancy.Projects.Api.GraphQL;
using Examples.Tenancy.Projects.Api.Lifecycle.Rest;
using Examples.Tenancy.Projects.Api.Operators.Rest;
using Examples.Tenancy.Projects.Api.Overview.Rest;
using Examples.Tenancy.Projects.Api.Ownership.Rest;
using Examples.Tenancy.Projects.Api.ProjectRoles.Rest;
using Examples.Tenancy.Projects.Application;
using Examples.Tenancy.Projects.Application.Access;
using Examples.Tenancy.Projects.Infrastructure;
using Examples.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Projects.Api;

/// <summary>
/// The Projects module, as a host adds it: one call that registers it, and one that maps its routes.
/// </summary>
/// <remarks>
/// The module's entry, and the one public type of this project. It lives here because a host should reference one
/// project of a module: the API project is what a host serves, so it composes the rest, the infrastructure
/// project's registration and the application project's. It is the only type in this project that names the
/// infrastructure project, and it only composes: it holds nothing, decides nothing, and no route is written here.
/// <para>
/// The routes are written per feature, in a folder named as the feature is in the application project:
/// <c>Overview</c>, <c>Lifecycle</c>, <c>Crew</c>, <c>Ownership</c>, <c>ProjectRoles</c>, <c>Access</c> and
/// <c>Operators</c>, each with its routes in <c>Rest</c>. Every one of them keeps to the same three things.
/// </para>
/// <list type="bullet">
/// <item>A route decides nothing. It makes a command or a query of its arguments and sends it, and takes only the
/// sender: never a handler, a use case or a port. What the request requires of the caller is checked on its way
/// to the handler, and what is refused is thrown as a coded <see cref="Exceptions.RefusalException"/>, which the
/// host turns into problem+json. So a route cannot forget a check, and the same request sent from anywhere else
/// (a seeder, a job, a GraphQL resolver) is refused exactly as it is here.</item>
/// <item>A body is always a record of this project, never a command: what a client may say is what the body has
/// a field for, and a field a command has for the application's own use, such as a project's id for an import,
/// stays out of reach.</item>
/// <item>The host decides what the group the routes are mapped into requires, a seat in the tenant the request
/// names; the module never names the host's filters.</item>
/// </list>
/// </remarks>
public static class ProjectsModule
{
    /// <summary>
    /// Registers Projects: how it is stored, the projects' rules, the checks its commands and queries pass, and the
    /// gate other modules ask. Its keys are not registered here: the host adds them with every module's, from the
    /// list the module marks with <c>[TenancyPermissions]</c>. The host says where its tables live and where
    /// what it publishes goes (see <see cref="ModuleHost"/>), and hands over the starter roles the application
    /// declares for its tenants' crews, which the projects' rules are made with. Tenancy must be registered first:
    /// Projects asks Tenancy's answers, and its context checks its saves with Tenancy's interceptor.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="host">The host's two decisions: the database, and the transport.</param>
    /// <param name="starterRoles">The starter project roles, one of them the crew lead's (<see cref="ProjectMembership"/>).</param>
    public static IServiceCollection AddProjectsModule(this IServiceCollection services, ModuleHost host, IReadOnlyList<StarterProjectRole> starterRoles)
    {
        var membership = new ProjectMembership(starterRoles);
        services.AddProjectsInfrastructure(host, membership).AddProjectsApplication(membership);

        // The module's GraphQL source schema, when the host serves GraphQL. No context is registered with it: a
        // field only sends, and every query that reads takes a context of its own from the pool.
        services.AddProjectsGraphQL(host);
        return services;
    }

    /// <summary>
    /// Maps every Projects route, feature by feature, each for a seat in the tenant the request selected:
    /// <c>/projects</c> to list, a page at a time, and open, <c>/projects/{id}</c> to read, rename, move, close and
    /// reopen one; <c>/projects/{id}/crew</c> to list its crew, put a seat on it and take it off, and
    /// <c>/projects/{id}/crew/{seatId}/roles</c> to give and take a member's project roles;
    /// <c>PUT /projects/{id}/owner</c>; <c>/project-roles</c> to list, make, rename, re-key and archive the
    /// tenant's project roles; and, for what the caller holds, <c>GET /access/projects/{id}?key=</c>,
    /// <c>GET /access/keys?keys=</c> and <c>POST /access/projects/keys</c>. The module never names the host's
    /// filters: it maps into the group it is given.
    /// </summary>
    /// <param name="seated">The group for callers with a seat. What it requires is the host's to decide.</param>
    public static IEndpointRouteBuilder MapProjectsModule(this IEndpointRouteBuilder seated)
        => seated
            .MapOverviewEndpoints()
            .MapLifecycleEndpoints()
            .MapCrewEndpoints()
            .MapOwnershipEndpoints()
            .MapProjectRolesEndpoints()
            .MapAccessEndpoints();

    /// <summary>
    /// Maps what the application's own staff read of the projects: <c>GET /operations/tenants/{tenant}/projects</c>,
    /// every project of one tenant. An operator holds no seat, so the host maps it outside the group that requires
    /// one.
    /// </summary>
    /// <param name="operators">The group for operators. What it requires is the host's to decide.</param>
    public static IEndpointRouteBuilder MapProjectsOperations(this IEndpointRouteBuilder operators)
        => operators.MapOperatorsEndpoints();
}
