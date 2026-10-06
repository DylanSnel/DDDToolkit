using DDDToolkit.EntityFramework;
using DDDToolkit.Supporting.Membership.EntityFramework;
using DDDToolkit.Supporting.Membership.Postgres;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Examples.Hosting;
using Examples.Tenancy.Projects.Infrastructure.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Projects.Infrastructure;

/// <summary>
/// Everything of Projects that knows how it is stored, registered by Projects: its context, the adapters over it
/// that implement the application project's two ports, the projects with the Membership package and its check of
/// the database, its outbox and the poller that empties it.
/// </summary>
/// <remarks>
/// Half of the module's composition. The other half is the application project's own registration, and the
/// module's entry, <c>AddProjectsModule</c> in the API project, calls both. That entry is the only code
/// outside this project that names it: the host references the API project alone, and nothing else in the API
/// project knows this one exists.
/// </remarks>
public static class ProjectsInfrastructure
{
    /// <summary>
    /// Registers how Projects is stored. The host says where its tables live and where what it publishes goes
    /// (see <see cref="ModuleHost"/>). Tenancy must be registered as well, by the Tenants module: Projects' context
    /// checks its saves with Tenancy's interceptor, which <c>AddTenancy</c> brings to every context, and its model,
    /// which keeps projects to a tenant, has its first save refused where nothing brought it. The module runs on
    /// Postgres and on nothing else, so the host is one built on its connections
    /// (<see cref="ModuleHost.OnPostgres"/>).
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="host">The host's two decisions: the database, and the transport.</param>
    /// <param name="membership">The projects' rules, which the projects are registered with.</param>
    /// <exception cref="InvalidOperationException">The host was not built on Postgres.</exception>
    public static IServiceCollection AddProjectsInfrastructure(this IServiceCollection services, ModuleHost host, Application.Access.ProjectMembership membership)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(membership);

        // The context, from a pool: the reads, which each take a context of their own, and the request's own
        // context both draw on it. The provider is the pools' to set, on the host's connections for requests or for
        // the background. The factory is the one dotnet ef and the export build the context with: its migrations are
        // what the host is checked against.
        //
        // One call wires it, each lock in its place: the toolkit's interceptors, then what the host's registrations
        // bring. Row level security, which the host registered, puts the caller's role and claims on every
        // connection, so the exported policies see who asks. Tenancy's save check, which AddTenancy brought, comes
        // after the toolkit's interceptors, so it sees what they let through, and refuses a project of another
        // tenant before anything is written.
        //
        // The default path, and no hold: a command's handler loads the project its request names, with the version
        // its caller named, and the save, the project's rules and the policies hold the write. A host that also
        // wants every save of a project tied to the version its request's check read adds .UseMemberHolds(application)
        // after UseDDDToolkit: the expert hold of the Membership package, which no handler writes a line for.
        host.RequirePostgres().AddContext<ProjectsContext, ProjectsContextFactory>(services, ProjectsContext.Schema, (application, options) => options
            .UseDDDToolkit(application));

        // The application's two ports. What a command changes goes through the request's context, its unit of
        // work, taken from the pool when the request first asks for it; what a query or an access check reads
        // takes a context of its own from the pool's factory, since the queries of one request may run side by side.
        services.AddScoped<IProjectStore, EfProjectStore>();
        services.AddScoped<IProjectReads, EfProjectReads>();

        // Some of what a request of this module requires is Tenancy's to decide: that the caller works in a tenant,
        // or is an operator. The package's check for those cases, added for this module's request interface and
        // over this module's context, which maps Tenancy's read model. Projects declares none of Tenancy's classes,
        // so the application's four ids are written out, as where the read model is mapped.
        services.AddTenancyAccess<TenantId, SeatId, OrganizationUnitId, RoleId, IProjectsRequest, ProjectsContext>();

        // The projects as the Membership package keeps them, over this module's context, with the projects' rules:
        // the access questions about a project, answered inside this context's statements, and the admission of a
        // seat or a project role to a crew. A crew member is a seat, so what the rules ask of the organization is
        // answered by a class the package's generator writes here from Tenancy's own answers. Projects declares
        // none of Tenancy's classes, so the ids that class cannot be given are written out. Then the package's
        // check, for a key held on a project, added for this module's request interface: who may change a crew,
        // or name an owner, is still what each command requires, here and in the database alike.
        services.AddProjectMembershipWithTenancy<ProjectsContext, TenantId, OrganizationUnitId, RoleId>(membership.Rules);
        services.AddProjectMemberAccess<IProjectsRequest>();

        // The Membership package's start-up check: the database answers the projects' membership as the rules
        // say it, with the four functions the policies of this module and of others ask, and the lock on the
        // crew's tables and on a project's owner. A database written from other rules does not let the host start;
        // the host runs the check with the others, before it takes a request.
        services.AddMembershipPostgres();

        // Projects' domain events are stored with the change that raised them, and delivered wherever the host
        // sends what modules publish. Generated when the module compiles: every domain event of the domain
        // project, under the name its module gives it, projects.project-opened and the rest.
        services.AddDDDToolkitEntityFramework(options => options.UseOutbox<ProjectsContext>(outbox =>
        {
            outbox.AddProjectsIntegrationEvents();
            host.Publish(outbox);
        }));

        // The poller marks each stored event handled. It needs no Tenancy caller: the outbox is not kept to a tenant.
        services.AddOutboxBackgroundService<ProjectsContext>(pollingInterval: TimeSpan.FromSeconds(1));

        return services;
    }
}
