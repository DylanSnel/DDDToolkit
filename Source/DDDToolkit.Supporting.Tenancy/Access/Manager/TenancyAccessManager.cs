using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Who acts and in which tenant, and the work that is not the caller's own. It is the core's <see cref="AccessManager"/>
/// with the tenant's members besides, so a Tenancy application takes this one thing. Your code takes it as
/// <c>TenancyAccessManager</c>, a class the generator writes, closed over your ids:
/// <code>
/// public sealed class AddMemberHandler(IBoardRepository boards, TenancyAccessManager access) : ICommandHandler&lt;AddMember&gt;
/// {
///     public async ValueTask&lt;Unit&gt; Handle(AddMember command, CancellationToken cancellationToken)
///     {
///         var tenant = access.CurrentTenant.Require();
///         // ...
///     }
/// }
/// </code>
/// <para>
/// The project that declares Tenancy's classes gets that class in the global namespace. A project of another module
/// sees only Tenancy's ids, and says which of its ids they are with one line,
/// <c>[assembly: TenancyIds&lt;TenantId, SeatId, OrganizationUnitId, RoleId&gt;]</c> (<see cref="TenancyIdsAttribute{TTenantId, TSeatId, TUnitId, TRoleId}"/>).
/// It then gets the class in its root namespace. <c>AddTenancy</c> registers the class of the module that declares the
/// classes, and <c>AddTenancyAccess</c> the class of the module it is called for. Both register it per scope.
/// </para>
/// <para>
/// It keeps no state of its own. <see cref="AccessManager.CurrentUser"/> and <see cref="CurrentTenant"/> read the current
/// flow of work each time they are asked, so they follow every <c>Begin</c> made after the manager was handed out.
/// </para>
/// <para>
/// <c>BeginSystem()</c>, <c>BeginSystem(scope)</c>, <see cref="BeginSystemIn"/> and
/// <see cref="BeginTokenIn"/> each start work that is not the caller's own, for one <c>using</c>. Each begins the tenant
/// the work acts in, or none, and the kind of work together: the kind decides how the work is recorded, and which
/// database role it runs as. The row access rules hold each of them. When the result is disposed, the user before it is current again.
/// Begin as narrow as possible: around the one call that needs it. <see cref="AccessManager.BeginAs(Guid)"/>, with a
/// user's id or a snapshot, <see cref="AccessManager.BeginSystemBypassingRowAccess"/> and
/// <see cref="AccessManager.BeginNone"/> are the core's, and leave the tenant as it is.
/// </para>
/// <para>
/// Tenancy's queries for the current caller come from the context you query: <c>db.Tenancy()</c>, which
/// <c>DDDToolkit.Supporting.Tenancy.EntityFramework</c> writes for your ids. A read source that is not Entity Framework
/// asks <see cref="In"/>.
/// </para>
/// </summary>
/// <typeparam name="TTenantId">The application's tenant id.</typeparam>
/// <typeparam name="TSeatId">The application's seat id.</typeparam>
/// <typeparam name="TUnitId">The application's unit id.</typeparam>
/// <typeparam name="TRoleId">The application's role id.</typeparam>
public class TenancyAccessManager<
    [TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId,
    [TemplateType(typeof(SeatAggregateAttribute<>))] TSeatId,
    [TemplateType(typeof(OrganizationUnitAttribute<>))] TUnitId,
    [TemplateType(typeof(RoleAggregateAttribute<>))] TRoleId> : AccessManager
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    private readonly TenancyCatalogue _catalogue;
    private readonly TimeProvider _clock;
    private readonly IServiceProvider _services;

    /// <summary>The access manager over the application's services. The container makes it; your code takes it.</summary>
    /// <param name="catalogue">The catalogue every permission asked about is checked against.</param>
    /// <param name="clock">What "now" is, for whether a grant applies.</param>
    /// <param name="services">
    /// The services of the scope: where <see cref="AccessManager.CurrentUser"/> comes from, the application's
    /// <see cref="ICurrentUser"/>, and the storage package's read of the tenants to visit.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public TenancyAccessManager(TenancyCatalogue catalogue, TimeProvider clock, IServiceProvider services)
        : base(CurrentUserOf(services))
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(clock);

        _catalogue = catalogue;
        _clock = clock;
        _services = services;
        CurrentTenant = new CurrentTenant<TTenantId, TSeatId>();
    }

    /// <summary>
    /// The tenant the current work acts in, and the seat in it. It reads the current flow of work each time it is
    /// asked. <c>Require()</c> returns the tenant, or throws the refusal the request got.
    /// </summary>
    /// <example>
    /// <code>
    /// var current = access.CurrentTenant.Require();   // current.Id is the tenant, current.Seat the caller's seat
    /// </code>
    /// </example>
    public CurrentTenant<TTenantId, TSeatId> CurrentTenant { get; }

    /// <summary>
    /// Begins system work outside any tenant in Tenancy's own scope, <c>tenancy</c>. It is for provisioning a tenant,
    /// when no tenant exists yet, so there is no tenant to begin in and no seat whose rights could do it. Of Tenancy's
    /// managers, only provisioning accepts it. The work reads nothing inside a tenant, and is recorded as the system in
    /// Tenancy's scope. It ends when the result is disposed.
    /// </summary>
    /// <remarks>
    /// It is <see cref="BeginSystem(string)"/> with Tenancy's scope. It does what
    /// <see cref="TenancyWork.BeginSystem{TTenantId, TSeatId}"/> does without a scope. The row access rules hold the
    /// work. On Postgres it runs as the toolkit's scoped system work. Under Tenancy's policies, work in the scope
    /// <c>tenancy</c> reads and writes nothing until provisioning narrows it to the tenant it makes.
    /// </remarks>
    /// <returns>The work, which ends when it is disposed. The user before it is current again then.</returns>
    /// <example>
    /// Read the signed-in user's id before the work begins. Inside it, <see cref="AccessManager.CurrentUser"/> reports
    /// system work and has no id.
    /// <code>
    /// var ada = access.CurrentUser.Id ?? throw new InvalidOperationException("[SignedIn] lets only a signed-in user through.");
    /// using (access.BeginSystem())
    /// {
    ///     var tenant = await tenants.ProvisionAsync(
    ///         new TenantToProvision("harbor-works", "Harbor Works", TenantShape.Hierarchical, "Harbor Works", ada),
    ///         cancellationToken);
    /// }
    /// </code>
    /// </example>
    public IDisposable BeginSystem() => BeginSystem(TenancyWork.SystemScope);

    /// <summary>
    /// Begins system work outside any tenant in <paramref name="scope"/>, such as a module's import that makes no
    /// tenant's rows. The work reads nothing inside a tenant, and is recorded as the system in its scope. It ends when
    /// the result is disposed.
    /// </summary>
    /// <remarks>
    /// It is the core's <see cref="AccessManager.BeginSystem(string)"/> with Tenancy's user besides, system work outside
    /// every tenant. It does what <see cref="TenancyWork.BeginSystem{TTenantId, TSeatId}"/> does. The row access rules
    /// hold the work. On Postgres it runs as the toolkit's scoped system work. A module's own work outside every tenant
    /// passes the module's name. Provisioning takes Tenancy's own scope, <see cref="BeginSystem()"/>.
    /// </remarks>
    /// <param name="scope">The scope of the work, such as the module's name: lower case letters, digits, <c>_</c> and <c>-</c>.</param>
    /// <returns>The work, which ends when it is disposed. The user before it is current again then.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is null. Nothing is begun then.</exception>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is not a scope. Nothing is begun then.</exception>
    /// <example>
    /// <code>
    /// using (access.BeginSystem("imports"))
    /// {
    ///     await imports.ReadCatalogueAsync(cancellationToken);
    /// }
    /// </code>
    /// </example>
    public override IDisposable BeginSystem(string scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return TenancyWork.BeginSystem<TTenantId, TSeatId>(scope);
    }

    /// <summary>
    /// Begins system work inside one tenant: seeding, an import, a job per tenant, or a right your code grants after a
    /// condition. The work holds every permission in that tenant and nothing in another. The last-administrator rule
    /// still applies to it. It ends when the result is disposed.
    /// <para>
    /// Take the tenant, and what the work changes, from the current tenant or from your own records. Never take them
    /// from the request: system work holds every permission in the tenant, so it changes whatever the request names.
    /// </para>
    /// </summary>
    /// <remarks>
    /// It does what <see cref="TenancyWork.BeginSystemIn{TTenantId, TSeatId}(TTenantId, TSeatId?, string)"/> does. The work
    /// is recorded as the system in <paramref name="scope"/>, and <paramref name="actingSeat"/> as who placed or
    /// granted. On Postgres it runs as the toolkit's scoped system work, a database role that cannot bypass the
    /// policies, so it reads and writes the rows of that tenant alone.
    /// </remarks>
    /// <param name="tenant">The tenant.</param>
    /// <param name="actingSeat">The seat of the person who asked for the work. It is only recorded in the history, and adds no rights.</param>
    /// <param name="scope">
    /// The scope of the work: Tenancy's own, which writes Tenancy's tables, unless a module's own work passes its name.
    /// Lower case letters, digits, <c>_</c> and <c>-</c>.
    /// </param>
    /// <returns>The work, which ends when it is disposed. The user before it is current again then.</returns>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is not a scope. Nothing is begun then.</exception>
    /// <example>
    /// A seat that passed a course gets the role the course gives. The seat holds no <c>tenancy.grants.manage</c>, so
    /// the handler grants the role as system work, for the calling seat and with the role from the application's own
    /// records:
    /// <code>
    /// var current = access.CurrentTenant.Require();
    /// using (access.BeginSystemIn(current.Id, actingSeat: current.Seat))
    /// {
    ///     await seats.GrantAsync(current.Seat!.Value, course.Role, course.Unit, until: null, reason: "Course passed", cancellationToken);
    /// }
    /// </code>
    /// </example>
    public IDisposable BeginSystemIn(TTenantId tenant, TSeatId? actingSeat = null, string scope = TenancyWork.SystemScope)
        => TenancyWork.BeginSystemIn<TTenantId, TSeatId>(tenant, actingSeat, scope);

    /// <summary>
    /// Begins system work inside one tenant that answers a link carrying a token a seat made, such as a feed only its
    /// owner knows the address of. It is recorded as the token and the seat it stands for. It holds what system work in
    /// the tenant holds, so your code narrows what it reads to that seat. Begin it only once the token was checked and
    /// found to be that seat's, and the seat to be in use. It ends when the result is disposed.
    /// </summary>
    /// <remarks>
    /// It does what <see cref="TenancyWork.BeginTokenIn{TTenantId, TSeatId}"/> does. On Postgres it runs as the toolkit's
    /// scoped system work in <paramref name="scope"/>, as <see cref="BeginSystemIn"/> does.
    /// </remarks>
    /// <param name="tenant">The tenant.</param>
    /// <param name="seat">The seat the token stands for.</param>
    /// <param name="scope">The scope of the work, the module's name: lower case letters, digits, <c>_</c> and <c>-</c>.</param>
    /// <returns>The work, which ends when it is disposed. The user before it is current again then.</returns>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is not a scope. Nothing is begun then.</exception>
    /// <example>
    /// A calendar feed whose address carries a token Leo made. The handler checks the token first, with the
    /// application's own records, and then reads Leo's inspections alone:
    /// <code>
    /// var link = await feeds.FindByTokenAsync(token, cancellationToken) ?? throw new FeedNotFoundException();
    /// using (access.BeginTokenIn(link.Tenant, link.Seat, scope: "inspections"))
    /// {
    ///     var due = await db.Inspections.Where(inspection => inspection.Inspector == link.Seat).ToListAsync(cancellationToken);
    /// }
    /// </code>
    /// </example>
    public IDisposable BeginTokenIn(TTenantId tenant, TSeatId seat, string scope)
        => TenancyWork.BeginTokenIn<TTenantId, TSeatId>(tenant, seat, scope);

    /// <summary>
    /// The ids of the tenants a round of system work visits: the active and the suspended ones, in no order. A job asks
    /// this first, and then begins system work in each tenant in turn, in its own scope. The read returns ids alone,
    /// which name no person and no row. It runs on a context of its own, outside the work around it, as the system in
    /// <paramref name="scope"/>.
    /// </summary>
    /// <param name="scope">The scope of the work that asks, the module's name: lower case letters, digits, <c>_</c> and <c>-</c>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The ids of the active and the suspended tenants.</returns>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is not a scope. Nothing is read then.</exception>
    /// <example>
    /// A nightly job closes overdue inspections in every tenant:
    /// <code>
    /// foreach (var tenant in await access.TenantsToSweepAsync("inspections", cancellationToken))
    /// {
    ///     using (access.BeginSystemIn(tenant, scope: "inspections"))
    ///     {
    ///         await inspections.CloseOverdueAsync(cancellationToken);
    ///     }
    /// }
    /// </code>
    /// </example>
    /// <exception cref="InvalidOperationException">
    /// No storage package registered the read: Tenancy is not registered with <c>AddTenancy</c> of
    /// <c>DDDToolkit.Supporting.Tenancy.EntityFramework</c>, or of another storage package that reads the tenants.
    /// </exception>
    public Task<IReadOnlyList<TTenantId>> TenantsToSweepAsync(string scope, CancellationToken cancellationToken)
    {
        var sweep = _services.GetService<ITenantsToSweep<TTenantId>>()
            ?? throw new InvalidOperationException(
                "No storage package registered the read of the tenants to visit, ITenantsToSweep<" + typeof(TTenantId).Name + ">. "
                + "Register Tenancy with services.AddTenancy<TContext>() of DDDToolkit.Supporting.Tenancy.EntityFramework.");

        return sweep.TenantsToSweepAsync(scope, cancellationToken);
    }

    /// <summary>
    /// The access queries for the current caller over a read source that is not Entity Framework, at this moment. Over a
    /// context, ask <c>db.Tenancy()</c> instead, which <c>DDDToolkit.Supporting.Tenancy.EntityFramework</c> writes for your
    /// ids.
    /// </summary>
    /// <param name="source">The rows to ask over.</param>
    /// <param name="executor">Runs the queries that answer yes or no.</param>
    /// <returns>
    /// The queries, for the caller of the current flow of work at this moment. They are composed over
    /// <paramref name="source"/> and run by whoever runs the query they end up in.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="executor"/> is null.</exception>
    /// <example>
    /// <code>
    /// var queries = access.In(readSource, executor);
    /// var editable = await executor.ListAsync(queries.UnitsWhereIHold("projects.edit"), cancellationToken);
    /// </code>
    /// </example>
    public ITenancyQueries<TTenantId, TSeatId, TUnitId, TRoleId> In(
        ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId> source,
        IQueryExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(executor);

        return new TenancyQueries<TTenantId, TSeatId, TUnitId, TRoleId>(
            source,
            executor,
            _catalogue,
            TenancyUsers.Current<TTenantId, TSeatId>(),
            _clock.GetUtcNow());
    }

    /// <summary>The application's current user, or the toolkit's over the host's options and source where none is registered.</summary>
    private static ICurrentUser CurrentUserOf(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.GetService<ICurrentUser>()
            ?? new DDDToolkit.Access.CurrentUser(services.GetService<CurrentUserOptions>(), services.GetService<ICurrentUserSource>());
    }
}
