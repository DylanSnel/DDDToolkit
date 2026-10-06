using Examples.Tenancy.Tenants.Api.Access.Rest;
using Examples.Tenancy.Tenants.Api.Catalogue.Rest;
using Examples.Tenancy.Tenants.Api.Directory.Rest;
using Examples.Tenancy.Tenants.Api.GraphQL;
using Examples.Tenancy.Tenants.Api.Grants.Rest;
using Examples.Tenancy.Tenants.Api.History.Rest;
using Examples.Tenancy.Tenants.Api.Invitations.Rest;
using Examples.Tenancy.Tenants.Api.Operators.Rest;
using Examples.Tenancy.Tenants.Api.Organization.Rest;
using Examples.Tenancy.Tenants.Api.Placements.Rest;
using Examples.Tenancy.Tenants.Api.Roles.Rest;
using Examples.Tenancy.Tenants.Api.Seats.Rest;
using Examples.Tenancy.Tenants.Api.Tenant.Rest;
using Examples.Tenancy.Tenants.Application;
using Examples.Tenancy.Tenants.Application.Invitations;
using Examples.Tenancy.Tenants.Infrastructure;
using Examples.Hosting;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tenants.Api;

/// <summary>
/// The Tenants module, as a host adds it: one call that registers it, and the calls that map its routes.
/// </summary>
/// <remarks>
/// The module's entry, and the one public type of this project. It lives here because a host should reference one
/// project of a module: the API project is what a host serves, so it composes the rest, the infrastructure
/// project's registration and the application project's. It is the only type in this project that names the
/// infrastructure project, and it only composes: it holds nothing, decides nothing, and no route is written here.
/// <para>
/// The routes are written per feature, in a folder named as the feature is in the application project:
/// <c>Seats</c>, <c>Organization</c>, <c>Tenant</c>, <c>Placements</c>, <c>Grants</c>, <c>Roles</c>,
/// <c>Invitations</c>, <c>Directory</c>, <c>Catalogue</c>, <c>Access</c>, <c>History</c> and <c>Operators</c>, each with its routes in <c>Rest</c>. It lives in the
/// module, not in a host, so every host that runs the module serves the same API. Every route keeps to the same
/// two things.
/// </para>
/// <list type="bullet">
/// <item>A route decides nothing. It makes a command or a query of its arguments and sends it, and takes only the
/// sender: never a handler, a use case or a port. What the request requires of the caller is checked on its way
/// to the handler, and what is refused is thrown as a coded <see cref="Exceptions.RefusalException"/>, which the
/// host turns into problem+json. So a route cannot forget a check, and the same request sent from anywhere else
/// (a seeder, a job, a GraphQL resolver) is refused exactly as it is here.</item>
/// <item>A body is always a record of this project, never a command: what a client may say is what the body has
/// a field for, and a field a command has for the application's own use, such as an id for an import, stays out
/// of reach.</item>
/// </list>
/// </remarks>
public static class TenantsModule
{
    /// <summary>
    /// The name of the tenant's administration schema: all of Tenancy a seat is offered at the gateway, and another
    /// person's roles besides. The host serves it on its own, with
    /// <c>MapGraphQL("/admin/graphql", AdministrationSchema)</c>, and keeps it out of what the gateway composes, or a
    /// client of the gateway would be offered its fields too.
    /// </summary>
    public static string AdministrationSchema => TenantsGraphQL.AdministrationSchemaName;

    /// <summary>
    /// Registers Tenancy: how it is stored, and the check its commands and queries pass. The host says where its
    /// tables live and where what it publishes goes (see <see cref="ModuleHost"/>), and passes the application's
    /// catalogue. Register it before the modules that ask Tenancy who may do what.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="host">The host's two decisions: the database, and the transport.</param>
    /// <param name="catalogue">The application's part of the permission catalogue.</param>
    public static IServiceCollection AddTenantsModule(this IServiceCollection services, ModuleHost host, ApplicationCatalogue catalogue)
    {
        services.AddTenantsInfrastructure(host, catalogue).AddTenantsApplication();

        // The module's GraphQL schemas, when the host serves GraphQL: its source schema, and the administration's,
        // which the host serves on its own (AdministrationSchema). No context is registered with them: a field only
        // sends, and every query that reads takes a context of its own from the pool.
        services.AddTenantsGraphQL(host);
        return services;
    }

    /// <summary>
    /// Tells the module where a person accepts an invitation: the address of the application's own page for it.
    /// A host with such a page calls this; the mail the identity provider sends a new account then leads to that
    /// page with the invitation's token after the <c>#</c>. Without it the mail only signs the person in, and
    /// whoever invited hands the token over.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="acceptPage">The page's address as a browser reaches it: absolute, http or https, without a fragment.</param>
    /// <exception cref="ArgumentException"><paramref name="acceptPage"/> is not such an address.</exception>
    public static IServiceCollection AddTenantsInvitationPage(this IServiceCollection services, Uri acceptPage)
        => services.AddSingleton(new InvitationPage(acceptPage));

    /// <summary>
    /// Maps <c>GET /me/seats</c>, a person's own seats in every tenant. It needs a token and no tenant, so the
    /// host maps it into a group that requires only that the caller is signed in.
    /// </summary>
    /// <param name="signedIn">The group for every signed-in caller. What it requires is the host's to decide.</param>
    public static IEndpointRouteBuilder MapTenantsSeatsOfMine(this IEndpointRouteBuilder signedIn)
        => signedIn.MapSeatsOfMine();

    /// <summary>
    /// Maps <c>POST /invitations/accept</c>, with which an invited person gets their seat. Like a person's own
    /// seats it needs a token and no tenant: whoever accepts has no seat there yet, and the invitation's token
    /// says which tenant. So the host maps it into the group that requires only that the caller is signed in.
    /// </summary>
    /// <param name="signedIn">The group for every signed-in caller. What it requires is the host's to decide.</param>
    public static IEndpointRouteBuilder MapTenantsInvitationAcceptance(this IEndpointRouteBuilder signedIn)
        => signedIn.MapInvitationAcceptance();

    /// <summary>
    /// Maps every other Tenancy route, feature by feature, each for a seat in the tenant the request selected:
    /// <c>GET /me</c>; <c>/tenancy/units</c> to list, add, move and archive units, and <c>POST /tenancy/shape</c>;
    /// <c>/tenancy/seats</c> to list seats, place and withdraw them, grant and revoke roles, and suspend,
    /// reactivate or deactivate them; <c>/tenancy/roles</c> to list, create, re-key and archive roles;
    /// <c>/tenancy/invitations</c> to invite a person by address, list the open
    /// invitations and revoke one; <c>POST /tenancy/directory/seats</c>, <c>/units</c> and <c>/roles</c>, which
    /// answer names by id; <c>GET /tenancy/catalogue</c>; <c>GET /access/units?key=</c>, where the caller
    /// holds a key; and <c>GET /tenancy/history</c>, who changed whose access. The module never names the host's
    /// filters: it maps into the group it is given.
    /// </summary>
    /// <param name="seated">The group for callers with a seat. What it requires is the host's to decide.</param>
    public static IEndpointRouteBuilder MapTenantsModule(this IEndpointRouteBuilder seated)
        => seated
            .MapSeatsEndpoints()
            .MapOrganizationEndpoints()
            .MapTenantEndpoints()
            .MapPlacementsEndpoints()
            .MapGrantsEndpoints()
            .MapRolesEndpoints()
            .MapInvitationsEndpoints()
            .MapDirectoryEndpoints()
            .MapCatalogueEndpoints()
            .MapAccessEndpoints()
            .MapHistoryEndpoints();

    /// <summary>
    /// Maps what the application's own staff read of Tenancy: <c>GET /operations/tenants</c>, every tenant with its
    /// active seats, and <c>GET /operations/tenants/{tenant}/history</c>, one tenant's access history. An operator
    /// holds no seat, so the host maps these outside the group that requires one.
    /// </summary>
    /// <param name="operators">The group for operators. What it requires is the host's to decide.</param>
    public static IEndpointRouteBuilder MapTenantsOperations(this IEndpointRouteBuilder operators)
        => operators.MapOperatorsEndpoints();
}
