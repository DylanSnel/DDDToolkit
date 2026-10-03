using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.UseCases;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Registration of Tenancy's storage-agnostic parts. The storage package registers the rest, the store and
/// the seat directory, and calls <see cref="AddTenancyCore"/> itself; an application calls the storage
/// package's registration, and <see cref="AddTenancyPermissions"/> from every module that owns keys.
/// </summary>
public static class TenancyServiceCollectionExtensions
{
    /// <summary>
    /// Registers the catalogue, the access answers, tenant selection and the use cases, everything except
    /// the store and the seat directory.
    /// <list type="bullet">
    /// <item>The catalogue is built once, from <see cref="TenancyOptions{TTenantId, TSeatId, TUnitId, TRoleId}.Catalogue"/>
    /// and every <see cref="PermissionContribution"/>, the first time it is asked for; resolve it at start-up to
    /// have a catalogue that does not hold together stop the application there.</item>
    /// <item>The answers are one for the application; selection and the use cases are made per scope.</item>
    /// <item>The toolkit's caller accessor a host registered before stays; otherwise the ambient caller is read.</item>
    /// <item>A <see cref="TimeProvider"/> registered before stays; otherwise the system clock is used.</item>
    /// </list>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets the options; every one but the token roles that hold seats is required.</param>
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
            options.Catalogue!,
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
    /// Adds a module's permission keys to the catalogue, next to the code that asks for them. Call it from
    /// any module, as often as needed: contributions add up. A key may be declared only once in the whole
    /// catalogue, and never under <c>tenancy.</c>.
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
}
