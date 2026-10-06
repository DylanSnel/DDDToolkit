using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.UseCases;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Registration of Tenancy's storage-agnostic parts. The storage package registers the rest, the store and
/// the seat directory, and calls <see cref="AddTenancyCore"/> itself; an application calls the storage
/// package's registration, and adds its modules' keys once, from the host: <c>services.AddTenancyPermissionsOfModules()</c>,
/// which Tenancy's generator writes from the lists the modules mark with <see cref="TenancyPermissionsAttribute"/>.
/// <see cref="AddTenancyPermissions"/> adds a list by hand.
/// </summary>
public static class TenancyServiceCollectionExtensions
{
    /// <summary>
    /// Registers the catalogue, the access answers, tenant selection and the use cases, everything except
    /// the store and the seat directory.
    /// <list type="bullet">
    /// <item>The catalogue is built once, from <see cref="TenancyOptions{TTenantId, TSeatId, TUnitId, TRoleId}.Catalogue"/>,
    /// or from none when it is not set, and every <see cref="PermissionContribution"/>, the first time it is asked
    /// for; resolve it at start-up to have a catalogue that does not hold together stop the application there.</item>
    /// <item>The answers are one for the application; selection and the use cases are made per scope.</item>
    /// <item>The toolkit's caller accessor a host registered before stays; otherwise the ambient caller is read.</item>
    /// <item>A <see cref="TimeProvider"/> registered before stays; otherwise the system clock is used.</item>
    /// </list>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets the options; the ways to make each id are required, the rest have defaults.</param>
    /// <exception cref="InvalidOperationException">
    /// An option is not set, and every missing one is named; or a token role is an operator's and seated as well.
    /// </exception>
    public static IServiceCollection AddTenancyCore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>(
        this IServiceCollection services,
        Action<TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>> configure)
        where TTenant : TenantAggregate<TTenantId>
        where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
        where TUnit : OrganizationUnitEntity<TUnitId>
        where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
        where TRole : RoleAggregate<TRoleId, TTenantId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>();
        configure(options);

        var missing = options.Missing();
        if (missing.Count > 0)
        {
            throw TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>.MissingOptions(missing);
        }

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

        // Who is calling, for the one use case that asks the toolkit's own caller: the tenants' directory, which is
        // an operator's. A host that knows its callers registered its own already.
        services.TryAddSingleton<ICallerAccessor, AmbientCallerAccessor>();
        services.AddSingleton(serviceProvider => TenancyCatalogue.Build(
            options.Catalogue ?? new ApplicationCatalogue(),
            serviceProvider.GetServices<PermissionContribution>().SelectMany(contribution => contribution.Permissions)));
        services.AddSingleton<ITenancyAnswers<TTenantId, TSeatId, TUnitId, TRoleId>, TenancyAnswers<TTenantId, TSeatId, TUnitId, TRoleId>>();
        services.AddScoped<TenantSelection<TTenantId, TSeatId>>();

        services.AddScoped<TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.TenantCommands>();
        services.AddScoped<TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.OrganizationCommands>();
        services.AddScoped<TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.SeatCommands>();
        services.AddScoped<TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.RoleCommands>();
        services.AddScoped<TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.TenancyDirectory>();
        services.AddScoped<TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.TenantDirectory>();

        return services;
    }

    /// <summary>
    /// Registers the invitation use cases, closed over the application's invitation class, for an application
    /// that invites people into a tenant: everything of invitations except their store, which the storage package
    /// registers; that package calls this itself. Call it after <see cref="AddTenancyCore"/>, which registers what
    /// the use cases share: the catalogue, the options, the clock and who is calling.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets how an invitation's id is made, which is required, and how long one stays open.</param>
    /// <exception cref="InvalidOperationException">
    /// Tenancy itself is not registered yet; no way to make an invitation's id is set; or the lifetimes are out of
    /// order.
    /// </exception>
    public static IServiceCollection AddTenancyInvitationsCore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId, TInvitation, TInvitationId>(
        this IServiceCollection services,
        Action<TenancyInvitationOptions<TInvitationId>> configure)
        where TTenant : TenantAggregate<TTenantId>
        where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
        where TUnit : OrganizationUnitEntity<TUnitId>
        where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
        where TRole : RoleAggregate<TRoleId, TTenantId>
        where TInvitation : InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
        where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        if (!services.Any(registered => registered.ServiceType == typeof(TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>)))
        {
            throw new InvalidOperationException(
                "Tenancy's invitations are registered after Tenancy itself, whose catalogue, options and clock they share: call AddTenancy first, and AddTenancyInvitations after it.");
        }

        var options = new TenancyInvitationOptions<TInvitationId>();
        configure(options);
        if (options.Problem() is { } problem)
        {
            throw TenancyInvitationOptions<TInvitationId>.Invalid(problem);
        }

        services.AddSingleton(options);
        services.AddScoped<TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.InvitationCommands<TInvitation, TInvitationId>>();
        return services;
    }

    /// <summary>
    /// Adds permission keys to the catalogue by hand. Contributions add up. A key may be declared only once in
    /// the whole catalogue, and never under <c>tenancy.</c>.
    /// <para>
    /// A module states its keys once, on a static list it marks with <see cref="TenancyPermissionsAttribute"/>,
    /// and the host adds every module's list with the call Tenancy's generator writes into it,
    /// <c>services.AddTenancyPermissionsOfModules()</c>, which calls this; an export builds the same catalogue from
    /// <c>TenancyPermissionsOfModules.All</c>. So a module that marks its list does not call this as well: the same
    /// list added twice stops the catalogue when it is built. This is for keys no generator collects, such as
    /// those of a module the host composes by hand.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="permissions">The module's keys.</param>
    public static IServiceCollection AddTenancyPermissions(this IServiceCollection services, IEnumerable<Permission> permissions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(permissions);

        services.AddSingleton(new PermissionContribution(permissions));
        return services;
    }

    /// <summary>
    /// Brings every tenant's roles up to the packs they were made from, in the background, once the host has
    /// started: after its start-up checks, when it runs them, and after every hosted service has started. A role
    /// made from a pack gets the keys the pack gained since the role was made or last followed it, a module's new
    /// keys in an administrators' pack that lists none among them, and loses the keys the pack lost; what the
    /// tenant changed itself stays. Each role that changes raises
    /// <see cref="RoleFollowedItsPack{TTenantId, TRoleId, TSeatId}"/>, for the host to tell the tenant's
    /// administrators; the package sends no message itself.
    /// <code>
    /// builder.Services.RunStartupChecks();
    /// builder.Services.SyncRolePacks();
    /// </code>
    /// <para>
    /// It runs <see cref="IRolePackSync"/>, which Tenancy's storage package registers with <c>AddTenancy</c>, once per
    /// start of the host, as Tenancy's system work in each tenant, one tenant after the other. A second run changes
    /// nothing, and so does a run on another instance of the host at the same time as this one: whichever commits
    /// second finds the roles following their packs already. A tenant it cannot sync is logged and left for the next
    /// start, and the host keeps serving meanwhile. A host that wants it elsewhere, in a deployment step or behind an
    /// operator's endpoint, leaves this call out and runs <see cref="IRolePackSync"/> itself.
    /// </para>
    /// <para>
    /// It is a call of its own, and not a start-up check: a check reads and changes nothing, and stops a start that
    /// would go wrong, while this changes the tenants' roles, and the host may serve without it. Calling it more than
    /// once runs it once.
    /// </para>
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection SyncRolePacks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RolePackSyncService>());
        return services;
    }
}
