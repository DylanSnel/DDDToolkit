using DDDToolkit.Access;
using DDDToolkit.EntityFramework;
using Examples.Tenancy.Tenants.Application.Access;
using Examples.Tenancy.Tenants.Application.Roles;
using Examples.Hosting;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Examples.Tenancy.Tenants.Infrastructure;

/// <summary>
/// Everything of the application's tenancy that knows how it is stored, registered by the module: its context,
/// the Tenancy package closed over this module's classes, the adapter of the application project's port, the
/// answer to the one question other modules put to this one, and its outbox with the poller that empties it. The
/// checks that tenancy holds together and that the database is set up for it come with the packages'
/// registrations, and the host runs them before it starts.
/// </summary>
/// <remarks>
/// Half of the module's composition. The other half is the application project's own registration, and the
/// module's entry, <c>AddTenantsModule</c> in the API project, calls both. That entry is the only code
/// outside this project that names it: the host references the API project alone, and nothing else in the API
/// project knows this one exists.
/// </remarks>
public static class TenantsInfrastructure
{
    /// <summary>
    /// Registers how Tenancy is stored. The host says where its tables live and where what it publishes goes (see
    /// <see cref="ModuleHost"/>), and passes the application's part of the catalogue: the packs a new tenant's
    /// roles are copied from, and the keys of other modules it marks as managing access. It is the application's
    /// data, so it lives in the composition root, not here; the other modules state their keys themselves, on the
    /// lists they mark with <c>[TenancyPermissions]</c>, and the host adds them all with
    /// <c>services.AddTenancyPermissionsOfModules()</c>, which Tenancy's generator writes into it.
    /// <para>
    /// The module runs on Postgres and on nothing else, so the host is one built on its connections
    /// (<see cref="ModuleHost.OnPostgres"/>), and has registered row level security, with
    /// <c>AddSupabaseRowLevelSecurity</c>, which <c>UseDDDToolkit</c> puts on the context here: it runs every command
    /// as the caller.
    /// </para>
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="host">The host's two decisions: the database, and the transport.</param>
    /// <param name="catalogue">The application's part of the catalogue.</param>
    /// <exception cref="InvalidOperationException">The host was not built on Postgres.</exception>
    public static IServiceCollection AddTenantsInfrastructure(this IServiceCollection services, ModuleHost host, ApplicationCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(catalogue);

        // The context, from a pool: the reads, which each take a context of their own, and the request's own
        // context both draw on it. The options are built once per pool, with the application's services, and every
        // interceptor in them asks who is calling when it is used. The provider is the pools' to set: one data source
        // for requests and one for the background, shared by every module. The factory is the one dotnet ef and the
        // export build the context with: its migrations are what the host is checked against.
        //
        // One call wires it, each lock in its place: the toolkit's interceptors, then the caller's role and claims on
        // every connection, which the host's row level security brings, so the policies see who asks, then Tenancy's
        // save check, which AddTenancy below brings, and which so sees what the toolkit let through. Here, in
        // Tenancy's own context, it also writes the closure of every organization a save changes, which the access
        // questions read; in the other modules' contexts it keeps their rows to the caller's tenant.
        host.RequirePostgres().AddContext<TenantsContext, TenantsContextFactory>(services, TenantsContext.Schema, (application, options) => options
            .UseDDDToolkit(application));

        // The tenant of Tenancy's caller on every connection, the rights left to the database, whose trigger
        // writes them, and every flow of work required to say who it runs as. It brings Tenancy's start-up checks
        // of the database: the scoped system role stays in its tenant, the reads across tenants answer, and the
        // policies, functions and the index on a tenant's root are the ones this catalogue writes. The host runs
        // them, with the checks the row level security and the migrations brought, before it starts.
        services.AddTenancyPostgres();

        // Generated into this project and closed over the module's classes, like modelBuilder.AddTenancy(): the use
        // cases, the access questions, tenant selection, the store over this context and the save interceptor. A new
        // id is the id's own, made in code before the save: TenantId.Create(), a time-ordered Guid the generator
        // writes for every id of the contracts project. It brings the start-up checks that the catalogue builds, that
        // every context that keeps rows to a tenant checks its saves, and the warning for a key a role holds that the
        // catalogue has lost.
        services.AddTenancy<TenantsContext>(options =>
        {
            options.Catalogue = catalogue;

            // The application's own staff: a token with this role holds no seat anywhere, and reads every tenant.
            // It runs as a database role of its own, which the exported policies let read and never write.
            options.OperatorTokenRoles.Add(SampleTokenRoles.Operator);
        });

        // Invitations, generated like AddTenancy and closed over the module's classes: the invitation class and
        // its id are named. Their use cases, and the store that keeps an invitation apart from its token's digest.
        // An invitation stays open for the package's seven days.
        services.AddTenancyInvitations<Invitation, InvitationId, TenantsContext>();

        // What a request of this module requires of its caller is one of Tenancy's cases, and the package's check
        // decides them: added for the module's request interface, generated like AddTenancy and closed over the
        // module's four ids. A key for the whole tenant is asked over this module's context, on one of the pool's.
        services.AddTenancyAccess<ITenantsRequest, TenantsContext>();

        // The application's port: where its queries read, each on a context of its own from the pool. What the
        // Tenancy package answers itself it answers in a scope of its own, whose context is one of the pool's too.
        services.AddScoped<ITenancyReads, EfTenancyReads>();

        // Who is asking. The host registers the accessor that reads the request's bearer token before this, and
        // TryAdd lets it win; without one, as in a test that begins its callers by hand, the ambient caller answers.
        services.TryAddSingleton<ICallerAccessor, AmbientCallerAccessor>();

        // Tenancy's domain events are stored with the change that raised them, and none of them leaves: they are
        // the package's, not a contract, so each is kept off the sinks. An integration event mapped before
        // AddTenancyDomainEvents would leave; the sample publishes none. A module that reacted to one would say
        // where it registers its handlers what they run as, with Around: system work in the tenant the message
        // names, since the host requires every flow of work to say who it runs as.
        services.AddDDDToolkitEntityFramework(options => options.UseOutbox<TenantsContext>(outbox =>
        {
            // What changes who may do what is kept in the access history as well, by the save that stores it in
            // the outbox, and each row says who made the change. A rename changes nobody's access, and is left out.
            // A role that followed its pack, once the host's SyncRolePacks() found the pack changed, is among what is
            // kept: it is how the sample tells a tenant's administrators, who read it on the History page, with the
            // keys that came in and went out. A host that mails them maps the event to a contract of its own.
            // An invitation's own events are stored and kept off the sinks like the rest, and are no part of the
            // history: what an acceptance changes is in it as the seat's own events.
            outbox.AddTenancyDomainEvents<TenantId, SeatId, OrganizationUnitId, RoleId>()
                .AddTenancyInvitationEvents<TenantId, InvitationId, OrganizationUnitId, RoleId, SeatId>()
                .KeepEventLog(log => log.AddTenancyEventLog<TenantId, SeatId, OrganizationUnitId, RoleId>());
            host.Publish(outbox);
        }));

        // The poller marks each stored event handled. It needs no Tenancy caller: the outbox is not kept to a tenant.
        services.AddOutboxBackgroundService<TenantsContext>(pollingInterval: TimeSpan.FromSeconds(1));

        return services;
    }
}
