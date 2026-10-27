using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Who acts and in which tenant, and the work that is not the caller's own. Your code takes it as
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
/// It keeps no state of its own. <see cref="CurrentUser"/> and <see cref="CurrentTenant"/> read the current flow of work
/// each time they are asked, so they follow every <c>Begin</c> made after the manager was handed out.
/// </para>
/// <para>
/// Each <c>Begin</c> method starts work that is not the caller's own, for one <c>using</c>. It begins the tenant the work
/// acts in and the kind of work together: the kind decides how the work is recorded, and which database role it runs as.
/// When the result is disposed, the caller before it is current again. Begin as narrow as possible: around the one call
/// that needs it.
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
    [TemplateType(typeof(RoleAggregateAttribute<>))] TRoleId>
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
    /// The services of the scope: where <see cref="CurrentUser"/> finds who the application acts for, the host's
    /// <see cref="ICallerAccessor"/>, and the storage package's read of the tenants to visit.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public TenancyAccessManager(TenancyCatalogue catalogue, TimeProvider clock, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(services);

        _catalogue = catalogue;
        _clock = clock;
        _services = services;
        CurrentUser = new CurrentUser(services);
        CurrentTenant = new CurrentTenant<TTenantId, TSeatId>();
    }

    /// <summary>Who acts: a signed-in user, somebody who has not signed in, or system work.</summary>
    public CurrentUser CurrentUser { get; }

    /// <summary>The tenant the current work acts in, and the seat in it.</summary>
    public CurrentTenant<TTenantId, TSeatId> CurrentTenant { get; }

    /// <summary>
    /// Begins system work outside any tenant, for provisioning a tenant: when no tenant exists yet, so there is no
    /// tenant to begin in and no seat whose rights could do it. Only provisioning accepts it. The work reads nothing
    /// inside a tenant, and is recorded as the system. It ends when the result is disposed.
    /// </summary>
    /// <remarks>It does what <see cref="TenancyWork.BeginSystem{TTenantId, TSeatId}"/> does.</remarks>
    public IDisposable BeginSystem() => TenancyWork.BeginSystem<TTenantId, TSeatId>();

    /// <summary>
    /// Begins system work inside one tenant: seeding, an import, a job per tenant, or a right your code grants after a
    /// condition. The work holds every permission in that tenant and nothing in another. The last-administrator rule
    /// still applies to it. It ends when the result is disposed.
    /// <para>
    /// Take the tenant, and what the work changes, from the current tenant or from your own records, never from the
    /// request: system work does what it is told.
    /// </para>
    /// </summary>
    /// <remarks>It does what <see cref="TenancyWork.BeginSystemIn{TTenantId, TSeatId}(TTenantId, TSeatId?, string)"/> does.</remarks>
    /// <param name="tenant">The tenant.</param>
    /// <param name="actingSeat">The seat of the person who asked for the work. It is only recorded in the history, and adds no rights.</param>
    /// <param name="scope">
    /// The scope of the work: Tenancy's own, which writes Tenancy's tables, unless a module's own work passes its name.
    /// Lower case letters, digits, <c>_</c> and <c>-</c>.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is not a scope. Nothing is begun then.</exception>
    public IDisposable BeginSystemIn(TTenantId tenant, TSeatId? actingSeat = null, string scope = TenancyWork.SystemScope)
        => TenancyWork.BeginSystemIn<TTenantId, TSeatId>(tenant, actingSeat, scope);

    /// <summary>
    /// Begins system work inside one tenant that answers a link carrying a token a seat made, such as a feed only its
    /// owner knows the address of. It is recorded as the token and the seat it stands for. It holds what system work in
    /// the tenant holds, so your code narrows what it reads to that seat. Begin it only once the token was checked and
    /// found to be that seat's, and the seat to be in use. It ends when the result is disposed.
    /// </summary>
    /// <remarks>It does what <see cref="TenancyWork.BeginTokenIn{TTenantId, TSeatId}"/> does.</remarks>
    /// <param name="tenant">The tenant.</param>
    /// <param name="seat">The seat the token stands for.</param>
    /// <param name="scope">The scope of the work, the module's name: lower case letters, digits, <c>_</c> and <c>-</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is not a scope. Nothing is begun then.</exception>
    public IDisposable BeginTokenIn(TTenantId tenant, TSeatId seat, string scope)
        => TenancyWork.BeginTokenIn<TTenantId, TSeatId>(tenant, seat, scope);

    /// <summary>
    /// The ids of the tenants a round of system work visits: the active and the suspended ones, in no order. A job asks
    /// this first, and then begins system work in each tenant in turn, in its own scope:
    /// <code>
    /// foreach (var tenant in await access.TenantsToSweepAsync("inspections", cancellationToken))
    /// {
    ///     using (access.BeginSystemIn(tenant, scope: "inspections"))
    ///     {
    ///         await inspections.CloseOverdueAsync(cancellationToken);
    ///     }
    /// }
    /// </code>
    /// The ids are all it learns. They name no person and no row. The read runs on a context of its own, so it does not
    /// reach into the work around it.
    /// </summary>
    /// <param name="scope">The scope of the work that asks, the module's name: lower case letters, digits, <c>_</c> and <c>-</c>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is not a scope. Nothing is read then.</exception>
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
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="executor"/> is null.</exception>
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
            TenancyCallers.Current<TTenantId, TSeatId>(),
            _clock.GetUtcNow());
    }
}
