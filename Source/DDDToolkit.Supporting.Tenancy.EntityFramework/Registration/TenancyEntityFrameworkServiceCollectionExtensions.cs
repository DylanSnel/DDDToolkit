using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.EventLog;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.UseCases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>Registers Tenancy, stored with Entity Framework.</summary>
public static class TenancyEntityFrameworkServiceCollectionExtensions
{
    /// <summary>
    /// Registers Tenancy over <typeparamref name="TContext"/>, the context whose model calls <c>AddTenancy</c>:
    /// everything <see cref="TenancyServiceCollectionExtensions.AddTenancyCore"/> registers, the store the use
    /// cases load and save through, the seat directory tenant selection asks, the save interceptor, brought to
    /// every context <c>UseDDDToolkit</c> wires as Tenancy's part (<see cref="TenancySaveInterceptor.PartName"/>),
    /// the query executor, and what an event log asks: who acted, as Tenancy knows the caller
    /// (<see cref="TenancyActedByAccessor"/>, put around the accessor registered before this call, or around the
    /// toolkit's default), and the tenant of each row of the log <c>AddTenancyEventLogTable</c> maps.
    /// <code>
    /// services.AddTenancy&lt;TenancyContext&gt;(options =&gt;
    ///     options.Catalogue = ShopCatalogue.Application);   // optional: packs, keys of its own, marks
    /// services.AddDbContext&lt;TenancyContext&gt;((serviceProvider, options) =&gt; options
    ///     .UseNpgsql(connectionString)
    ///     .UseDDDToolkit(serviceProvider));
    /// </code>
    /// Every option has a default, so <c>services.AddTenancy&lt;TenancyContext&gt;()</c> is enough. A new id is made by
    /// the id itself, <c>TSeatId.Create()</c>, in code and before the save: the generator writes it for an id over a
    /// <see cref="Guid"/>, and an id over anything else declares its own.
    /// The store and the directory are scoped, like the context they use. The store offers a save that fails
    /// to every registered <see cref="ITenancySaveFailures"/>, which a package for one database adds, and
    /// leaves to the database what <see cref="TenancyStoreOptions"/> says, which such a package sets.
    /// <para>
    /// <c>AddTenancy&lt;TenancyContext&gt;</c> is generated into the project that declares the classes, closed over
    /// them (<see cref="TemplateRegistrationAttribute"/>), or into a project of the same <c>[assembly: Module]</c>
    /// that declares none, such as the module's infrastructure project next to its domain project. It calls this
    /// method with the classes and ids filled in; the context is still the caller's to name. This method can be
    /// called as well, with all ten.
    /// </para>
    /// <para>
    /// It brings the start-up checks of <see cref="TenancyChecks"/>, which a host runs with
    /// <c>services.RunStartupChecks()</c>: the catalogue builds, every context that keeps rows to a tenant checks
    /// its saves, and a key a role holds that the catalogue has lost is logged.
    /// </para>
    /// <para>
    /// And it registers <see cref="IRolePackSync"/> over the context, which brings every tenant's roles up to the
    /// packs they were made from: <c>services.SyncRolePacks()</c> runs it once the host has started, and a host may
    /// run it itself.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Changes Tenancy's options, every one of which has a default; leave it out to keep them all.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A token role is an operator's and seated as well.</exception>
    [TemplateRegistration]
    public static IServiceCollection AddTenancy<
        [TemplateType(typeof(TenantAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TTenant,
        [TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId,
        [TemplateType(typeof(OrganizationAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TOrganization,
        [TemplateType(typeof(OrganizationUnitAttribute<>), Take = TemplateArgumentKind.Type)] TUnit,
        [TemplateType(typeof(OrganizationUnitAttribute<>))] TUnitId,
        [TemplateType(typeof(SeatAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TSeat,
        [TemplateType(typeof(SeatAggregateAttribute<>))] TSeatId,
        [TemplateType(typeof(RoleAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TRole,
        [TemplateType(typeof(RoleAggregateAttribute<>))] TRoleId,
        TContext>(
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
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTenancyCore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>(configure);

        // What the store leaves to the database: nothing, unless a package for one database says otherwise.
        services.AddOptions<TenancyStoreOptions>();

        services.AddScoped<TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.IStore,
            EfTenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId, TContext>>();
        services.AddScoped<ISeatDirectory<TTenantId, TSeatId>,
            EfSeatDirectory<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRoleId, TContext>>();

        // What services.SyncRolePacks() runs once the host has started, and a host runs itself from a deployment step
        // or an operator's endpoint: every tenant's roles follow their packs, tenant by tenant, over this context.
        services.TryAddSingleton<IRolePackSync,
            EfRolePackSync<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId, TContext>>();

        // One interceptor for every context: it keeps no state, and every context that keeps entities to a
        // tenant adds the same one. UseDDDToolkit adds it to every context it wires, as Tenancy's part.
        services.TryAddSingleton<TenancySaveInterceptor,
            TenancySaveInterceptor<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>>();
        TenancyDbContextOptionsExtensions.AddPart(services);
        services.TryAddSingleton(EfQueryExecutor.Instance);
        services.TryAddSingleton<IQueryExecutor>(EfQueryExecutor.Instance);

        // Who acted, for an event log: what Tenancy knows of the caller, around whatever accessor was registered
        // before. And the tenant of every row of Tenancy's own log. Both are singletons that read what follows
        // the flow of work, so a context taken from a pool asks them like any other.
        TenancyActedByAccessor.Decorate(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEventLogFields, TenancyEventLogFields>());

        // Before the host serves anything, once it runs its checks: the catalogue holds together, every context is
        // wired, and no role holds a key the catalogue has lost.
        TenancyChecks.AddStartupChecks<TRole, TRoleId, TTenantId, TContext>(services);

        return services;
    }

    /// <summary>
    /// Registers invitations over <typeparamref name="TContext"/>, the context whose model calls
    /// <c>AddTenancyInvitations</c>, for an application that invites people into a tenant: the invitation use
    /// cases (<c>InvitationCommands</c>) and the store they load and keep invitations through. Call it after
    /// <c>AddTenancy</c>, with the application's invitation class, its id and the context:
    /// <code>
    /// services.AddTenancy&lt;TenancyContext&gt;();
    /// services.AddTenancyInvitations&lt;ShopInvitation, InvitationId, TenancyContext&gt;();
    /// </code>
    /// <para>
    /// That call is generated into the project that declares Tenancy's classes, closed over them, as
    /// <c>AddTenancy&lt;TenancyContext&gt;</c> is (<see cref="TemplateRegistrationAttribute"/>). The invitation
    /// class and its id are named in the call, since an application may have none. This method can be called as
    /// well, with all twelve. A new invitation's id is made by the id itself, <c>InvitationId.Create()</c>.
    /// </para>
    /// <para>
    /// The store is scoped, and works in the unit of work of Tenancy's own store: an invitation is saved, and its
    /// acceptance with the seat it makes, by that store's one save.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Changes how long an invitation stays open, which has defaults; leave it out to keep them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Tenancy itself is not registered yet; or the lifetimes are out of order.</exception>
    [TemplateRegistration]
    public static IServiceCollection AddTenancyInvitations<
        [TemplateType(typeof(TenantAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TTenant,
        [TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId,
        [TemplateType(typeof(OrganizationAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TOrganization,
        [TemplateType(typeof(OrganizationUnitAttribute<>), Take = TemplateArgumentKind.Type)] TUnit,
        [TemplateType(typeof(OrganizationUnitAttribute<>))] TUnitId,
        [TemplateType(typeof(SeatAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TSeat,
        [TemplateType(typeof(SeatAggregateAttribute<>))] TSeatId,
        [TemplateType(typeof(RoleAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TRole,
        [TemplateType(typeof(RoleAggregateAttribute<>))] TRoleId,
        TInvitation,
        TInvitationId,
        TContext>(
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
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTenancyInvitationsCore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId, TInvitation, TInvitationId>(configure);
        services.AddScoped<TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.IInvitationStore<TInvitation, TInvitationId>,
            EfInvitationStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId, TInvitation, TInvitationId, TContext>>();

        return services;
    }

    /// <summary>
    /// Adds Tenancy's access check to the checks the requests of one module are held to
    /// (<see cref="AccessChecks{TRequests}"/>): what decides the cases of <see cref="TenancyRequirement"/> a
    /// request of that module declares, before its handler runs.
    /// <code>
    /// services.AddTenancyAccess&lt;IBillingRequest, BillingContext&gt;();
    /// </code>
    /// A module calls it for its own request interface and its own context, once, next to the checks for the
    /// cases only it has. The check asks a key for the whole tenant over <typeparamref name="TContext"/>, so
    /// that is Tenancy's own context or one that maps Tenancy's read model (<c>AddTenancyReadModel</c>, or
    /// <c>AddTenancyReadFunctions</c> where the database keeps the rights), and so is a key at a unit
    /// (<see cref="TenancyRequirement.AtUnit{TUnitId}"/>, closed over <typeparamref name="TUnitId"/>); a module
    /// whose requests declare neither is never read for, whatever its context maps. The
    /// statement runs on a context of its own where the application registered a factory for the context
    /// (<c>IDbContextFactory&lt;TContext&gt;</c>), a pooled one say, and on the scope's own context otherwise.
    /// <para>
    /// The check is free of any dispatcher, as the package is: whatever calls
    /// <see cref="AccessChecks{TRequests}.RequireAsync"/> in front of the module's handlers holds the requests
    /// to it. In an application that uses the Mediator library that is the pipeline behavior the generator
    /// writes for an interface marked <c>[AccessRequests]</c>.
    /// </para>
    /// <para>
    /// <c>AddTenancyAccess&lt;IBillingRequest, BillingContext&gt;</c> is generated into the project that declares
    /// Tenancy's classes, closed over their four ids (<see cref="TemplateRegistrationAttribute"/>), or into a
    /// project of the same <c>[assembly: Module]</c> that declares none. A module that only asks Tenancy
    /// declares none of the classes and calls this method itself, with the application's four ids written out,
    /// as it maps the read model. Tenancy itself is registered by <c>AddTenancy</c>, in the same container;
    /// adding the check for an interface more than once is harmless.
    /// </para>
    /// </summary>
    /// <typeparam name="TTenantId">The application's tenant id.</typeparam>
    /// <typeparam name="TSeatId">The application's seat id.</typeparam>
    /// <typeparam name="TUnitId">The application's unit id.</typeparam>
    /// <typeparam name="TRoleId">The application's role id.</typeparam>
    /// <typeparam name="TRequests">The module's request interface, which every command and query of the module implements.</typeparam>
    /// <typeparam name="TContext">The module's context, which a key for the whole tenant is asked over.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    [TemplateRegistration]
    public static IServiceCollection AddTenancyAccess<
        [TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId,
        [TemplateType(typeof(SeatAggregateAttribute<>))] TSeatId,
        [TemplateType(typeof(OrganizationUnitAttribute<>))] TUnitId,
        [TemplateType(typeof(RoleAggregateAttribute<>))] TRoleId,
        TRequests,
        TContext>(
        this IServiceCollection services)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
        where TRequests : class, IRequireAccess
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddAccessCheck<TRequests, TenancyAccessCheck<TTenantId, TSeatId, TUnitId, TRoleId, TContext>>();
    }
}
