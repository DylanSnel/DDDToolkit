using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Registration of Tenancy's storage-agnostic parts. The storage package registers the rest, the store and
/// the seat directory, and calls <see cref="AddTenancyCore"/> itself; an application calls the storage
/// package's registration, and adds its modules' permissions once, from the host: <c>services.AddTenancyPermissionsOfModules()</c>,
/// which Tenancy's generator writes from the lists the modules mark with <see cref="TenancyPermissionsAttribute"/>.
/// <see cref="AddTenancyPermissions"/> adds a list by hand.
/// </summary>
public static class TenancyServiceCollectionExtensions
{
    /// <summary>
    /// Registers the catalogue, the access manager, tenant selection and the managers, everything except the store and
    /// the seat directory.
    /// <list type="bullet">
    /// <item>The catalogue is built once, from <see cref="TenancyOptions{TTenantId, TSeatId, TUnitId, TRoleId}.Catalogue"/>,
    /// or from none when it is not set, and every <see cref="PermissionContribution"/>, the first time it is asked
    /// for; resolve it at start-up to have a catalogue that does not hold together stop the application there.</item>
    /// <item>The access manager, <see cref="TenancyAccessManager{TTenantId, TSeatId, TUnitId, TRoleId}"/>, selection and the
    /// managers are made per scope. The storage package's <c>AddTenancy</c> registers the application's own
    /// <c>TenancyAccessManager</c>, <c>TenantManager</c>, <c>OrganizationManager</c>, <c>SeatManager</c> and <c>RoleManager</c> beside them. Selection is registered without its id types as well, <see cref="ITenantSelection"/>,
    /// the same instance, unless a host registered its own before: that one stays.</item>
    /// <item>The toolkit's caller accessor a host registered before stays; otherwise the ambient caller is read.</item>
    /// <item>What the caller's own seat is, for a request about it (<c>[OrOwn] SeatId Seat</c>, <c>TenancyAccess.Own(seat)</c>):
    /// the caller's seat in the tenant, unless a host registered its own before with <c>AddCallersOwn</c>.</item>
    /// <item>A <see cref="TimeProvider"/> registered before stays; otherwise the system clock is used.</item>
    /// <item>A new id is made by the id itself, <c>TTenantId.Create()</c>, which every id of the application's
    /// classes has: the generator writes it for an id over a <see cref="Guid"/>, and DDD00067 asks for it on any
    /// other.</item>
    /// </list>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Changes the options, every one of which has a default; leave it out to keep them all.</param>
    /// <exception cref="InvalidOperationException">A token role is an operator's and seated as well.</exception>
    public static IServiceCollection AddTenancyCore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>(
        this IServiceCollection services,
        Action<TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>>? configure = null)
        where TTenant : TenantAggregate<TTenantId>
        where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
        where TUnit : OrganizationUnitEntity<TUnitId>
        where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
        where TRole : RoleAggregate<TRoleId, TTenantId>
        where TTenantId : struct, ICreatableEntityId<TTenantId>, IEquatable<TTenantId>
        where TUnitId : struct, ICreatableEntityId<TUnitId>, IEquatable<TUnitId>
        where TSeatId : struct, ICreatableEntityId<TSeatId>, IEquatable<TSeatId>
        where TRoleId : struct, ICreatableEntityId<TRoleId>, IEquatable<TRoleId>
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>();
        configure?.Invoke(options);

        // An operator looks across tenants and a seat acts in one: a token role that was both would be a seat
        // with every tenant's rows in reach of its database role.
        if (options.SeatedOperators() is { Count: > 0 } both)
        {
            throw new InvalidOperationException(
                "These token roles are operators' and hold seats as well: " + string.Join(", ", both)
                + ". An operator holds no seat: take the role out of TenantSelection.SeatedTokenRoles, or out of OperatorTokenRoles.");
        }

        services.AddSingleton(options);
        services.AddSingleton(options.TenantSelection);
        services.AddSingleton(new TenancyOperatorTokenRoles(options.OperatorTokenRoles));
        services.TryAddSingleton(TimeProvider.System);

        // Who is calling, for the reads and the commands that ask the toolkit's own caller: the operators' tenant list and
        // accepting an invitation. A host that knows its callers registered its own already.
        services.TryAddSingleton<ICallerAccessor, AmbientCallerAccessor>();
        services.AddSingleton(serviceProvider => TenancyCatalogue.Build(
            options.Catalogue ?? new ApplicationCatalogue(),
            serviceProvider.GetServices<PermissionContribution>().SelectMany(contribution => contribution.Permissions)));
        services.TryAddScoped<TenancyAccessManager<TTenantId, TSeatId, TUnitId, TRoleId>>();

        // What a request about the caller's own seat compares its seat with, [OrOwn] SeatId: the caller's seat in the
        // tenant. A host that says it otherwise registered its own already, with AddCallersOwn, and that one stays.
        services.TryAddSingleton<ICallersOwn<TSeatId>, CallersSeat<TTenantId, TSeatId>>();
        services.AddScoped<TenantSelection<TTenantId, TSeatId>>();

        // The same selection without its id types, for the host's middleware, which only makes current the caller it
        // is answered: one instance per scope, asked by either name. A host that answers it itself registered its
        // own already, and that one stays.
        services.TryAddScoped<ITenantSelection>(serviceProvider => serviceProvider.GetRequiredService<TenantSelection<TTenantId, TSeatId>>());

        // The managers, closed over the application's classes. The storage package's AddTenancy registers the application's
        // own beside them, the classes the generator writes, which code takes.
        services.TryAddScoped<TenantManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>>();
        services.TryAddScoped<OrganizationManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>>();
        services.TryAddScoped<SeatManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>>();
        services.TryAddScoped<RoleManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>>();

        return services;
    }

    /// <summary>
    /// Registers the invitation manager, closed over the application's invitation class, for an application
    /// that invites people into a tenant: everything of invitations except their store, which the storage package
    /// registers; that package calls this itself. Call it after <see cref="AddTenancyCore"/>, which registers what
    /// the managers share: the catalogue, the options, the clock and who is calling.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Changes how long an invitation stays open, which has defaults; leave it out to keep them.</param>
    /// <exception cref="InvalidOperationException">Tenancy itself is not registered yet; or the lifetimes are out of order.</exception>
    public static IServiceCollection AddTenancyInvitationsCore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId, TInvitation, TInvitationId>(
        this IServiceCollection services,
        Action<TenancyInvitationOptions<TInvitationId>>? configure = null)
        where TTenant : TenantAggregate<TTenantId>
        where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
        where TUnit : OrganizationUnitEntity<TUnitId>
        where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
        where TRole : RoleAggregate<TRoleId, TTenantId>
        where TInvitation : InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>
        where TTenantId : struct, ICreatableEntityId<TTenantId>, IEquatable<TTenantId>
        where TUnitId : struct, ICreatableEntityId<TUnitId>, IEquatable<TUnitId>
        where TSeatId : struct, ICreatableEntityId<TSeatId>, IEquatable<TSeatId>
        where TRoleId : struct, ICreatableEntityId<TRoleId>, IEquatable<TRoleId>
        where TInvitationId : struct, ICreatableEntityId<TInvitationId>, IEquatable<TInvitationId>
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(registered => registered.ServiceType == typeof(TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>)))
        {
            throw new InvalidOperationException(
                "Tenancy's invitations are registered after Tenancy itself, whose catalogue, options and clock they share: call AddTenancy first, and AddTenancyInvitations after it.");
        }

        var options = new TenancyInvitationOptions<TInvitationId>();
        configure?.Invoke(options);
        if (options.Problem() is { } problem)
        {
            throw TenancyInvitationOptions<TInvitationId>.Invalid(problem);
        }

        services.AddSingleton(options);
        services.TryAddScoped<InvitationManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId, TInvitation, TInvitationId>>();
        return services;
    }

    /// <summary>
    /// Registers the check of Tenancy's operator cases for the requests of <typeparamref name="TRequests"/>:
    /// <see cref="TenancyRequirement.Operator"/> and <see cref="TenancyRequirement.OperatorIn{TTenantId}"/>, which a request
    /// declares with <see cref="OperatorAttribute"/>. It refuses every caller who is no operator, and runs a command an
    /// operator sends as operator work. The storage package's <c>AddTenancyAccess</c> calls it beside the check of Tenancy's
    /// other cases, so an application calls that one. Adding it twice adds it once.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <typeparam name="TTenantId">The application's tenant id.</typeparam>
    /// <typeparam name="TSeatId">The application's seat id.</typeparam>
    /// <typeparam name="TUnitId">The application's unit id.</typeparam>
    /// <typeparam name="TRoleId">The application's role id.</typeparam>
    /// <typeparam name="TRequests">The module's request interface.</typeparam>
    public static IServiceCollection AddTenancyOperatorAccess<TTenantId, TSeatId, TUnitId, TRoleId, TRequests>(this IServiceCollection services)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
        where TRequests : class, IRequireAccess
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddAccessCheck<TRequests, TenancyOperatorCheck<TTenantId, TSeatId, TUnitId, TRoleId>>();
    }

    /// <summary>
    /// Adds permissions to the catalogue by hand. Contributions add up. A permission may be declared only once in
    /// the whole catalogue, and never under <c>tenancy.</c>.
    /// <para>
    /// A module declares its permissions once, as the constants of a static class marked <c>[Permissions]</c>, and
    /// the host adds every module's with the call Tenancy's generator writes into it,
    /// <c>services.AddTenancyPermissionsOfModules()</c>, which calls this; an export builds the same catalogue from
    /// <c>TenancyPermissionsOfModules.All</c>. So a module's permissions are not added with this as well: the same
    /// list added twice stops the catalogue when it is built. This is for permissions no generator collects, such as
    /// those of a module the host composes by hand.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="permissions">The module's permissions.</param>
    public static IServiceCollection AddTenancyPermissions(this IServiceCollection services, IEnumerable<Permission> permissions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(permissions);

        services.AddSingleton(new PermissionContribution(permissions));
        return services;
    }

    /// <summary>
    /// Brings every tenant's roles up to the starter roles they were made from, in the background, once the host has
    /// started: after its start-up checks, when it runs them, and after every hosted service has started. A role
    /// made from a starter role gets the permissions the starter role gained since the role was made or last followed it, a module's new
    /// permissions in an administrators' starter role that lists none among them, and loses the permissions the starter role lost; what the
    /// tenant changed itself stays. Each role that changes raises
    /// <see cref="RoleFollowedItsStarterRole{TTenantId, TRoleId, TSeatId}"/>, for the host to tell the tenant's
    /// administrators; the package sends no message itself.
    /// <code>
    /// builder.Services.RunStartupChecks();
    /// builder.Services.SyncStarterRoles();
    /// </code>
    /// <para>
    /// It runs <see cref="IStarterRoleSync"/>, which Tenancy's storage package registers with <c>AddTenancy</c>, once per
    /// start of the host, as Tenancy's system work in each tenant, one tenant after the other. A second run changes
    /// nothing, and so does a run on another instance of the host at the same time as this one: whichever commits
    /// second finds the roles following their starter roles already. A tenant it cannot sync is logged and left for the next
    /// start, and the host keeps serving meanwhile. A host that wants it elsewhere, in a deployment step or behind an
    /// operator's endpoint, leaves this call out and runs <see cref="IStarterRoleSync"/> itself.
    /// </para>
    /// <para>
    /// It is a call of its own, and not a start-up check: a check reads and changes nothing, and stops a start that
    /// would go wrong, while this changes the tenants' roles, and the host may serve without it. Calling it more than
    /// once runs it once.
    /// </para>
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection SyncStarterRoles(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, StarterRoleSyncService>());
        return services;
    }
}
