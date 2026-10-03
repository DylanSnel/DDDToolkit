using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.EntityFramework.EventLog;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>Chooses which of Tenancy's domain events its access history keeps.</summary>
public static class TenancyEventLogExtensions
{
    /// <summary>
    /// Keeps, in the event log of Tenancy's context, every event of Tenancy's that changes who may do what: the
    /// history a tenant's access is answered for afterwards.
    /// <code>
    /// options.UseOutbox&lt;TenancyContext&gt;(outbox =&gt; outbox
    ///     .AddTenancyDomainEvents&lt;TenantId, SeatId, OrganizationUnitId, RoleId&gt;()
    ///     .KeepEventLog(log =&gt; log.AddTenancyEventLog&lt;TenantId, SeatId, OrganizationUnitId, RoleId&gt;()));
    /// </code>
    /// <list type="bullet">
    /// <item>A tenant provisioned, activated, suspended, reactivated or closed, and its shape changed.</item>
    /// <item>A unit added, moved or archived.</item>
    /// <item>A seat added, suspended, reactivated or deactivated; placed in a unit, withdrawn from one, and its primary placement changed.</item>
    /// <item>A role granted to a seat or revoked from it.</item>
    /// <item>A role created, its keys changed, with the keys that came in and went out, or archived.</item>
    /// </list>
    /// <para>
    /// A rename changes nobody's access, so the four events that say a tenant's organization, a unit, a seat or a
    /// role is called something else are left out. They are still stored in the outbox like the rest.
    /// </para>
    /// <para>
    /// Each row is the event as the outbox stores it, written by the save that raised it: the ids, the keys, the
    /// dates and who made the change (<c>By</c>, which every event of Tenancy's carries), and never what a
    /// tenant, a unit, a seat or a role is called. The row's own columns say the same actor again and the tenant,
    /// so a history is read and kept apart per tenant without opening a payload. Map the table with
    /// <c>modelBuilder.AddTenancyEventLogTable(Database)</c>.
    /// </para>
    /// <para>
    /// The choices of an event log add up: an event of the application's own that belongs in the history is kept
    /// with one more <c>Keep</c> next to this call, and calling <c>KeepEventLog()</c> with nothing chosen keeps
    /// every event of the context instead, renames included.
    /// </para>
    /// </summary>
    /// <typeparam name="TTenantId">The application's tenant id.</typeparam>
    /// <typeparam name="TSeatId">The application's seat id.</typeparam>
    /// <typeparam name="TUnitId">The application's unit id.</typeparam>
    /// <typeparam name="TRoleId">The application's role id.</typeparam>
    /// <param name="log">The event log of Tenancy's outbox, as <c>KeepEventLog</c> hands it over.</param>
    /// <exception cref="ArgumentNullException"><paramref name="log"/> is null.</exception>
    public static EventLogOptions AddTenancyEventLog<TTenantId, TSeatId, TUnitId, TRoleId>(this EventLogOptions log)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        ArgumentNullException.ThrowIfNull(log);

        // The tenant itself: whether anything in it counts at all.
        log.Keep<TenantProvisioned<TTenantId, TSeatId>>();
        log.Keep<TenantActivated<TTenantId, TSeatId>>();
        log.Keep<TenantSuspended<TTenantId, TSeatId>>();
        log.Keep<TenantReactivated<TTenantId, TSeatId>>();
        log.Keep<TenantClosed<TTenantId, TSeatId>>();
        log.Keep<TenantShapeChanged<TTenantId, TSeatId>>();

        // The tree: how far down a role given at a unit counts.
        log.Keep<OrganizationUnitAdded<TTenantId, TUnitId, TSeatId>>();
        log.Keep<OrganizationUnitMoved<TTenantId, TUnitId, TSeatId>>();
        log.Keep<OrganizationUnitArchived<TTenantId, TUnitId, TSeatId>>();

        // Seats: who is there, where, and with which roles.
        log.Keep<SeatAdded<TTenantId, TSeatId>>();
        log.Keep<SeatSuspended<TTenantId, TSeatId>>();
        log.Keep<SeatReactivated<TTenantId, TSeatId>>();
        log.Keep<SeatDeactivated<TTenantId, TSeatId>>();
        log.Keep<SeatPlaced<TTenantId, TSeatId, TUnitId>>();
        log.Keep<SeatWithdrawn<TTenantId, TSeatId, TUnitId>>();
        log.Keep<PrimaryPlacementChanged<TTenantId, TSeatId, TUnitId>>();
        log.Keep<OrganizationRoleGranted<TTenantId, TSeatId, TUnitId, TRoleId>>();
        log.Keep<OrganizationRoleRevoked<TTenantId, TSeatId, TUnitId, TRoleId>>();

        // Roles: what a grant gives.
        log.Keep<RoleCreated<TTenantId, TRoleId, TSeatId>>();
        log.Keep<RoleKeysChanged<TTenantId, TRoleId, TSeatId>>();
        log.Keep<RoleArchived<TTenantId, TRoleId, TSeatId>>();

        return log;
    }
}
