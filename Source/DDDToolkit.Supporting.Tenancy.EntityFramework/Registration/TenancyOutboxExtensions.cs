using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>Registers Tenancy's domain events with the outbox of Tenancy's context.</summary>
public static class TenancyOutboxExtensions
{
    /// <summary>
    /// Registers every domain event Tenancy raises, closed over the application's ids, under the name its
    /// <c>[DomainEventName]</c> gives it (<c>tenancy.seat-placed</c> and the like), so the outbox stores and reads
    /// them. The package's events are generic, so no generated registration can find them.
    /// <para>
    /// They are domain events, not contracts: each one the application has not mapped to an integration event
    /// is kept off the sinks. To publish one, map it before this call:
    /// </para>
    /// <code>
    /// options.UseOutbox&lt;TenancyContext&gt;(outbox =&gt; outbox
    ///     .PublishAs&lt;SeatAdded&lt;TenantId, SeatId&gt;, SeatJoined&gt;(added =&gt; new SeatJoined(added.TenantId.Value, added.SeatId.Value))
    ///     .AddTenancyDomainEvents&lt;TenantId, SeatId, OrganizationUnitId, RoleId&gt;());
    /// </code>
    /// Mapped after it, an event is already kept off the sinks, and the mapping throws.
    /// <para>
    /// Every event carries who made the change as <c>By</c>, so every one of them is closed over the seat id as
    /// well, an event about a tenant, a unit or a role included: <c>TenantSuspended&lt;TenantId, SeatId&gt;</c>.
    /// To keep the ones that change access as a history, see
    /// <see cref="TenancyEventLogExtensions.AddTenancyEventLog{TTenantId, TSeatId, TUnitId, TRoleId}"/>.
    /// </para>
    /// </summary>
    /// <param name="outbox">The outbox of Tenancy's context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="outbox"/> is null.</exception>
    public static OutboxOptions AddTenancyDomainEvents<TTenantId, TSeatId, TUnitId, TRoleId>(this OutboxOptions outbox)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        ArgumentNullException.ThrowIfNull(outbox);

        Register<TenantProvisioned<TTenantId, TSeatId>>(outbox);
        Register<TenantActivated<TTenantId, TSeatId>>(outbox);
        Register<TenantSuspended<TTenantId, TSeatId>>(outbox);
        Register<TenantReactivated<TTenantId, TSeatId>>(outbox);
        Register<TenantClosed<TTenantId, TSeatId>>(outbox);
        Register<TenantShapeChanged<TTenantId, TSeatId>>(outbox);

        Register<OrganizationRenamed<TTenantId, TSeatId>>(outbox);
        Register<OrganizationUnitAdded<TTenantId, TUnitId, TSeatId>>(outbox);
        Register<OrganizationUnitRenamed<TTenantId, TUnitId, TSeatId>>(outbox);
        Register<OrganizationUnitMoved<TTenantId, TUnitId, TSeatId>>(outbox);
        Register<OrganizationUnitArchived<TTenantId, TUnitId, TSeatId>>(outbox);

        Register<SeatAdded<TTenantId, TSeatId>>(outbox);
        Register<SeatRenamed<TTenantId, TSeatId>>(outbox);
        Register<SeatSuspended<TTenantId, TSeatId>>(outbox);
        Register<SeatReactivated<TTenantId, TSeatId>>(outbox);
        Register<SeatDeactivated<TTenantId, TSeatId>>(outbox);
        Register<SeatPlaced<TTenantId, TSeatId, TUnitId>>(outbox);
        Register<SeatWithdrawn<TTenantId, TSeatId, TUnitId>>(outbox);
        Register<PrimaryPlacementChanged<TTenantId, TSeatId, TUnitId>>(outbox);
        Register<OrganizationRoleGranted<TTenantId, TSeatId, TUnitId, TRoleId>>(outbox);
        Register<OrganizationRoleRevoked<TTenantId, TSeatId, TUnitId, TRoleId>>(outbox);

        Register<RoleCreated<TTenantId, TRoleId, TSeatId>>(outbox);
        Register<RoleRenamed<TTenantId, TRoleId, TSeatId>>(outbox);
        Register<RoleKeysChanged<TTenantId, TRoleId, TSeatId>>(outbox);
        Register<RoleArchived<TTenantId, TRoleId, TSeatId>>(outbox);
        Register<RoleFollowedItsPack<TTenantId, TRoleId, TSeatId>>(outbox);

        return outbox;
    }

    /// <summary>
    /// Registers the domain events of invitations, closed over the application's ids, under the names their
    /// <c>[DomainEventName]</c> gives them: <c>tenancy.invitation-issued</c>, <c>tenancy.invitation-accepted</c>
    /// and <c>tenancy.invitation-cancelled</c>. Call it next to
    /// <see cref="AddTenancyDomainEvents{TTenantId, TSeatId, TUnitId, TRoleId}"/> in an application that maps
    /// invitations:
    /// <code>
    /// options.UseOutbox&lt;TenancyContext&gt;(outbox =&gt; outbox
    ///     .AddTenancyDomainEvents&lt;TenantId, SeatId, OrganizationUnitId, RoleId&gt;()
    ///     .AddTenancyInvitationEvents&lt;TenantId, InvitationId, OrganizationUnitId, RoleId, SeatId&gt;());
    /// </code>
    /// As the others, each one the application has not mapped to an integration event before this call is kept
    /// off the sinks. They carry ids and dates, and who made the change: never a token, a digest or an address.
    /// <para>
    /// None of them is among the events the access history keeps: an invitation changes nobody's access until it
    /// is accepted, and then the seat's own events, added, placed and granted its role, say what changed.
    /// </para>
    /// </summary>
    /// <param name="outbox">The outbox of Tenancy's context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="outbox"/> is null.</exception>
    public static OutboxOptions AddTenancyInvitationEvents<TTenantId, TInvitationId, TUnitId, TRoleId, TSeatId>(this OutboxOptions outbox)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        ArgumentNullException.ThrowIfNull(outbox);

        Register<InvitationIssued<TTenantId, TInvitationId, TUnitId, TRoleId, TSeatId>>(outbox);
        Register<InvitationAccepted<TTenantId, TInvitationId, TSeatId>>(outbox);
        Register<InvitationCancelled<TTenantId, TInvitationId, TSeatId>>(outbox);

        return outbox;
    }

    private static void Register<TEvent>(OutboxOptions outbox)
        where TEvent : IDomainEvent
    {
        outbox.RegisterEvent<TEvent>();
        if (!outbox.IntegrationEvents.IsMapped(typeof(TEvent)))
        {
            outbox.DoNotPublish<TEvent>();
        }
    }
}
