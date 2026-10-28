using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.Managers;

/// <summary>
/// People's seats: adding them, placing them in units, granting them roles there, and suspending, reactivating or
/// deactivating them; and what the screens show of them, who the caller is and what any seat may do where. Your code
/// takes it as <c>SeatManager</c>, a class the generator writes, closed over your classes and ids, and the container
/// makes per scope.
/// <para>
/// Each command takes the domain objects, <c>GrantAsync(seat, role, at, ...)</c>, or their ids, as a request carries
/// them. Both check the same: the manager loads the seat, the role and the unit itself, and asks the caller's rights
/// over what it loaded, never over the state of an object it was handed. Each command returns the seat as it changed it,
/// the one the unit of work keeps.
/// </para>
/// <para>
/// Holding <see cref="TenancyPermissions.GrantsManage"/> at a unit is enough to give roles there. Its holder
/// gives any active role of the tenant that manages no access to a seat placed there or below, and takes any
/// such role away, without holding the role's permissions: to another seat for as long as it says, to itself for no
/// longer than it holds <see cref="TenancyPermissions.GrantsManage"/> there. A role that manages access (see
/// <see cref="TenancyCatalogue.AccessManagingPermissionsOf"/>) stays contained: a seat gives it, and takes it
/// away, only while holding there each of its permissions that manage access, for at least as long as the grant
/// runs, and never gives it to itself. Withdrawing a placement takes its grants away, by the same rule, and
/// so does suspending or deactivating a seat, while reactivating one gives its grants back by that rule too.
/// System work in a tenant is not held to any of that. When the application turns containment off
/// (<see cref="ApplicationCatalogue.ContainAccessManagingPermissions"/>), a role that manages access goes as one
/// that manages none: a grants manager gives every role, to another seat for as long as it says and to itself
/// for no longer than it holds <see cref="TenancyPermissions.GrantsManage"/> there, and the permission a command asks where
/// it acts is all a seat's status takes. Nothing leaves a tenant that has an administrator
/// without one, whoever asks, and that is decided before the seat changes, so a refused command leaves
/// nothing for a later save to write. A seat of another tenant is not found.
/// </para>
/// <para>
/// Nothing here names a seat: a seat has no name in Tenancy. What the application keeps on its seat class, a
/// name it is shown by among it, it sets in the callback of <see cref="AddAsync"/>, and changes with a use
/// case of its own, under a rule of its own.
/// </para>
/// <para>
/// The reads answer seats as the application's own class, whole: the seat with its identity, its placements and their
/// grants and every field the application added. Each is read for the answer and tracked by nobody, so nothing done to
/// one is saved, by the read or by a save later in the same unit of work. Whoever works in a tenant reads it: a seat of
/// it, or system work in it. No permission is asked, so the request that asks says who may: what another seat holds
/// where, above all, is a choice of the application's. On Postgres a read rule of the application's on its seat class
/// may narrow which seats the caller reads.
/// </para>
/// </summary>
/// <param name="store">Where seats and roles are loaded and saved.</param>
/// <param name="catalogue">The permissions asked for, and which permissions are live.</param>
/// <param name="clock">What "now" is.</param>
public class SeatManager<
    [TemplateType(typeof(TenantAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TTenant,
    [TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId,
    [TemplateType(typeof(OrganizationAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TOrganization,
    [TemplateType(typeof(OrganizationUnitAttribute<>), Take = TemplateArgumentKind.Type)] TUnit,
    [TemplateType(typeof(OrganizationUnitAttribute<>))] TUnitId,
    [TemplateType(typeof(SeatAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TSeat,
    [TemplateType(typeof(SeatAggregateAttribute<>))] TSeatId,
    [TemplateType(typeof(RoleAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TRole,
    [TemplateType(typeof(RoleAggregateAttribute<>))] TRoleId>(
    ITenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId> store,
    TenancyCatalogue catalogue,
    TimeProvider clock)
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
    /// <summary>How many ids one question by id takes.</summary>
    public const int MostIds = TenancyReads.MostIds;

    private static readonly IComparer<TSeatId> IdOrder = TenancyReads.IdOrder<TSeatId>();

    // ------------------------------------------------------------------ commands

    /// <summary>Adds a seat for a verified identity, placed nowhere yet.</summary>
    /// <param name="identity">The person's verified identity, the subject of their token.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <param name="id">
    /// Its id, for imports and seeding; a new one otherwise. An id that is already a seat's is a mistake in
    /// the import, which the save reports.
    /// </param>
    /// <param name="configure">
    /// Sets the fields the application added to its seat class, such as the name it is shown by, on the new
    /// seat once the caller is checked and before the seat is handed to the store, so they are saved in the
    /// same transaction and the class's own rules judge them there. When it throws, the seat is not added:
    /// nothing is saved, by this call or by a later save in the same scope.
    /// </param>
    /// <returns>The new seat.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c> without <see cref="TenancyPermissions.SeatsManage"/> for the whole tenant,
    /// <c>tenancy.identity-has-seat</c>, <c>tenancy.identity-required</c>.
    /// </exception>
    /// <example>
    /// A seat for Leo, with the name the application's seat class keeps:
    /// <code>
    /// var leo = await seats.AddAsync(command.Identity, cancellationToken, configure: seat =&gt; seat.Rename("Leo"));
    /// </code>
    /// </example>
    public async Task<TSeat> AddAsync(Guid identity, CancellationToken cancellationToken, TSeatId? id = null, Action<TSeat>? configure = null)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();
        await gate.RequireAtTenantAsync(TenancyPermissions.SeatsManage, cancellationToken).ConfigureAwait(false);

        if (await store.IdentityHasSeatAsync(tenantId, identity, cancellationToken).ConfigureAwait(false))
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.IdentityHasSeat);
        }

        var seat = TenancyInstances.NewSeat<TSeat, TSeatId, TTenantId, TUnitId, TRoleId>(
            id ?? TSeatId.Create(), tenantId, identity, gate.By);

        // The application's own fields, before the store has the seat: a callback that throws leaves nothing
        // for this save or a later one to write, and what it sets is written with the seat.
        configure?.Invoke(seat);
        store.Add(seat);
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return seat;
    }

    /// <summary>Places a seat in an active unit.</summary>
    /// <param name="seat">The seat.</param>
    /// <param name="unit">The unit.</param>
    /// <param name="primary">Whether this becomes the seat's primary placement.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The seat, placed.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c> without <see cref="TenancyPermissions.SeatsManage"/> at the unit,
    /// <c>tenancy.seat-not-found</c>, <c>tenancy.unit-not-found</c>, <c>tenancy.unit-not-active</c>, and what
    /// the seat refuses: placing itself, a second placement in the unit, a second primary.
    /// </exception>
    /// <example>
    /// <code>
    /// var seat = await seats.PlaceAsync(command.Seat, command.Unit, command.Primary, cancellationToken);
    /// </code>
    /// </example>
    public async Task<TSeat> PlaceAsync(TSeatId seat, TUnitId unit, bool primary, CancellationToken cancellationToken)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();
        await gate.SerializeAsync(tenantId, cancellationToken).ConfigureAwait(false);
        await gate.RequireAtAsync(TenancyPermissions.SeatsManage, unit, cancellationToken).ConfigureAwait(false);

        var placed = await gate.LoadSeatAsync(seat, cancellationToken).ConfigureAwait(false);
        await gate.RequireActiveUnitAsync(tenantId, unit, cancellationToken).ConfigureAwait(false);

        placed.Place(unit, primary, gate.Now, gate.ActorFor(seat), gate.By);
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return placed;
    }

    /// <summary>Places a seat in an active unit, as <see cref="PlaceAsync(TSeatId, TUnitId, bool, CancellationToken)"/> does with their ids.</summary>
    /// <inheritdoc cref="PlaceAsync(TSeatId, TUnitId, bool, CancellationToken)"/>
    /// <example>
    /// <code>
    /// leo = await seats.PlaceAsync(leo, northCoast, primary: true, cancellationToken);
    /// </code>
    /// </example>
    public Task<TSeat> PlaceAsync(TSeat seat, TUnit unit, bool primary, CancellationToken cancellationToken)
        => PlaceAsync(IdOf(seat), IdOf(unit), primary, cancellationToken);

    /// <summary>
    /// Withdraws a seat from a unit, revoking every role it holds there. When it holds any, the caller needs
    /// <see cref="TenancyPermissions.GrantsManage"/> there too, and, while containment is on
    /// (<see cref="ApplicationCatalogue.ContainAccessManagingPermissions"/>), for each of those roles that manages
    /// access, its permissions that do, held there until at least that grant's end: taking a placement away takes its
    /// grants away. A role that manages no access needs none of its permissions.
    /// </summary>
    /// <param name="seat">The seat.</param>
    /// <param name="unit">The unit of one of its placements.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The seat, withdrawn.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c>, <c>tenancy.seat-not-found</c>, <c>tenancy.placement-not-found</c>,
    /// <c>tenancy.grant-exceeds-own</c>, <c>tenancy.last-admin</c>.
    /// </exception>
    /// <example>
    /// <code>
    /// var seat = await seats.WithdrawAsync(command.Seat, command.Unit, cancellationToken);
    /// </code>
    /// </example>
    public async Task<TSeat> WithdrawAsync(TSeatId seat, TUnitId unit, CancellationToken cancellationToken)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();
        await gate.SerializeAsync(tenantId, cancellationToken).ConfigureAwait(false);
        await gate.RequireAtAsync(TenancyPermissions.SeatsManage, unit, cancellationToken).ConfigureAwait(false);

        var withdrawn = await gate.LoadSeatAsync(seat, cancellationToken).ConfigureAwait(false);
        var placement = withdrawn.Placements.FirstOrDefault(candidate => candidate.UnitId.Equals(unit))
            ?? throw TenancyRefusals.Refuse(TenancyRefusals.PlacementNotFound);

        if (placement.Grants.Count > 0)
        {
            await gate.RequireAtAsync(TenancyPermissions.GrantsManage, unit, cancellationToken).ConfigureAwait(false);

            var taken = new List<(TRole Role, DateTimeOffset? Until)>();
            foreach (var grant in placement.Grants)
            {
                if (await store.FindRoleAsync(grant.RoleId, cancellationToken).ConfigureAwait(false) is { } role)
                {
                    taken.Add((role, grant.EndsAt));
                }
            }

            await gate.RequireRevocableAsync(taken, unit, cancellationToken).ConfigureAwait(false);
        }

        await gate.EnsureAdministratorRemainsAsync(tenantId, AdministratorLoss<TSeatId, TUnitId, TRoleId>.OfPlacement(seat, unit), cancellationToken).ConfigureAwait(false);
        withdrawn.Withdraw(unit, gate.By);
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return withdrawn;
    }

    /// <summary>Withdraws a seat from a unit, as <see cref="WithdrawAsync(TSeatId, TUnitId, CancellationToken)"/> does with their ids.</summary>
    /// <inheritdoc cref="WithdrawAsync(TSeatId, TUnitId, CancellationToken)"/>
    /// <example>
    /// Leo leaves North Coast, with the roles he held there:
    /// <code>
    /// leo = await seats.WithdrawAsync(leo, northCoast, cancellationToken);
    /// </code>
    /// </example>
    public Task<TSeat> WithdrawAsync(TSeat seat, TUnit unit, CancellationToken cancellationToken)
        => WithdrawAsync(IdOf(seat), IdOf(unit), cancellationToken);

    /// <summary>Makes a seat's placement in a unit its primary one.</summary>
    /// <param name="seat">The seat.</param>
    /// <param name="unit">The unit of one of its placements.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The seat, with its new primary placement.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c> without <see cref="TenancyPermissions.SeatsManage"/> at the unit,
    /// <c>tenancy.seat-not-found</c>, <c>tenancy.placement-not-found</c>.
    /// </exception>
    /// <example>
    /// <code>
    /// var seat = await seats.MakePrimaryAsync(command.Seat, command.Unit, cancellationToken);
    /// </code>
    /// </example>
    public async Task<TSeat> MakePrimaryAsync(TSeatId seat, TUnitId unit, CancellationToken cancellationToken)
    {
        var gate = Gate();
        gate.RequireTenant();
        await gate.RequireAtAsync(TenancyPermissions.SeatsManage, unit, cancellationToken).ConfigureAwait(false);

        var changed = await gate.LoadSeatAsync(seat, cancellationToken).ConfigureAwait(false);
        changed.MakePrimary(unit, gate.By);
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return changed;
    }

    /// <summary>Makes a seat's placement in a unit its primary one, as <see cref="MakePrimaryAsync(TSeatId, TUnitId, CancellationToken)"/> does with their ids.</summary>
    /// <inheritdoc cref="MakePrimaryAsync(TSeatId, TUnitId, CancellationToken)"/>
    /// <example>
    /// <code>
    /// leo = await seats.MakePrimaryAsync(leo, southBay, cancellationToken);
    /// </code>
    /// </example>
    public Task<TSeat> MakePrimaryAsync(TSeat seat, TUnit unit, CancellationToken cancellationToken)
        => MakePrimaryAsync(IdOf(seat), IdOf(unit), cancellationToken);

    /// <summary>
    /// Grants a seat a role at an active unit where it is placed, from now until <paramref name="until"/>.
    /// With <see cref="TenancyPermissions.GrantsManage"/> at the unit, a seat gives another seat any role that
    /// manages no access, for any period, and itself one for no longer than it holds
    /// <see cref="TenancyPermissions.GrantsManage"/> there. While containment is on
    /// (<see cref="ApplicationCatalogue.ContainAccessManagingPermissions"/>), a role that manages access it gives only
    /// while it holds there each of the role's permissions that manage access, for at least as long as the grant
    /// lasts, and never to itself: a seat whose own grant ends next week does not give such a role for good.
    /// Off, such a role goes as any other.
    /// </summary>
    /// <param name="seat">The seat.</param>
    /// <param name="role">The role, which must be active.</param>
    /// <param name="at">The unit of one of the seat's placements.</param>
    /// <param name="until">When the grant ends, or <see langword="null"/> for no end.</param>
    /// <param name="reason">Why, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <param name="from">
    /// When the grant starts, for imports and seeding: system work only. A grant a seat makes starts now,
    /// and a seat that passes this is refused rather than silently ignored.
    /// </param>
    /// <returns>The seat, with the grant.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.start-system-only</c>, <c>tenancy.not-permitted</c> without <see cref="TenancyPermissions.GrantsManage"/>
    /// at the unit, <c>tenancy.role-not-found</c>, <c>tenancy.self-appointment</c> for a role that manages
    /// access given to the caller itself while containment is on, <c>tenancy.grant-exceeds-own</c>,
    /// <c>tenancy.seat-not-found</c>,
    /// <c>tenancy.unit-not-found</c>, <c>tenancy.unit-not-active</c> for an archived unit,
    /// <c>tenancy.invalid-period</c>, and what the seat refuses.
    /// </exception>
    /// <example>
    /// <code>
    /// var seat = await seats.GrantAsync(command.Seat, command.Role, command.Unit, command.Until, command.Reason, cancellationToken);
    /// </code>
    /// </example>
    public async Task<TSeat> GrantAsync(
        TSeatId seat,
        TRoleId role,
        TUnitId at,
        DateTimeOffset? until,
        string? reason,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();
        if (from is not null && !gate.BySystem)
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.StartSystemOnly);
        }

        await gate.SerializeAsync(tenantId, cancellationToken).ConfigureAwait(false);
        await gate.RequireAtAsync(TenancyPermissions.GrantsManage, at, cancellationToken).ConfigureAwait(false);

        var granted = await gate.LoadRoleAsync(role, cancellationToken).ConfigureAwait(false);
        await gate.RequireGrantableAsync(seat, at, granted, until, cancellationToken).ConfigureAwait(false);

        var holder = await gate.LoadSeatAsync(seat, cancellationToken).ConfigureAwait(false);
        await gate.RequireActiveUnitAsync(tenantId, at, cancellationToken).ConfigureAwait(false);
        var period = GrantPeriod.Between(from ?? gate.Now, until);
        holder.Grant(at, role, granted.Facts, period, gate.ActorFor(seat), reason, gate.By);
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return holder;
    }

    /// <summary>
    /// Grants a seat a role at a unit, as <see cref="GrantAsync(TSeatId, TRoleId, TUnitId, DateTimeOffset?, string?, CancellationToken, DateTimeOffset?)"/>
    /// does with their ids.
    /// </summary>
    /// <inheritdoc cref="GrantAsync(TSeatId, TRoleId, TUnitId, DateTimeOffset?, string?, CancellationToken, DateTimeOffset?)"/>
    /// <example>
    /// Rhea gives Leo the role Surveyor at North, with no end:
    /// <code>
    /// leo = await seats.GrantAsync(leo, surveyor, at: north, until: null, reason: "Passed the survey course", cancellationToken);
    /// </code>
    /// </example>
    public Task<TSeat> GrantAsync(
        TSeat seat,
        TRole role,
        TUnit at,
        DateTimeOffset? until,
        string? reason,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null)
        => GrantAsync(IdOf(seat), IdOf(role), IdOf(at), until, reason, cancellationToken, from);

    /// <summary>
    /// Revokes a role a seat holds at a unit. With <see cref="TenancyPermissions.GrantsManage"/> at the unit, a seat
    /// takes away any role that manages no access, an archived role included, which manages nothing. While
    /// containment is on (<see cref="ApplicationCatalogue.ContainAccessManagingPermissions"/>), a role that manages
    /// access it takes away only while it holds there each of the role's permissions that manage access, until at
    /// least the grant's end, or for good for a grant with none; off, such a role goes as any other. A seat's
    /// own grant that applies now is its own hold, so a seat may take away its current roles.
    /// </summary>
    /// <param name="seat">The seat.</param>
    /// <param name="role">The role it holds there.</param>
    /// <param name="at">The unit it holds the role at.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The seat, without the grant.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c> without <see cref="TenancyPermissions.GrantsManage"/> at the unit,
    /// <c>tenancy.role-not-found</c>, <c>tenancy.seat-not-found</c>, <c>tenancy.grant-exceeds-own</c>,
    /// <c>tenancy.last-admin</c>, <c>tenancy.grant-not-found</c>.
    /// </exception>
    /// <example>
    /// <code>
    /// var seat = await seats.RevokeAsync(command.Seat, command.Role, command.Unit, cancellationToken);
    /// </code>
    /// </example>
    public async Task<TSeat> RevokeAsync(TSeatId seat, TRoleId role, TUnitId at, CancellationToken cancellationToken)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();
        await gate.SerializeAsync(tenantId, cancellationToken).ConfigureAwait(false);
        await gate.RequireAtAsync(TenancyPermissions.GrantsManage, at, cancellationToken).ConfigureAwait(false);

        var revoked = await gate.LoadRoleAsync(role, cancellationToken).ConfigureAwait(false);
        var holder = await gate.LoadSeatAsync(seat, cancellationToken).ConfigureAwait(false);

        // How long the grant runs is read from the seat. A missing grant is the seat's to refuse.
        if (holder.Placements.FirstOrDefault(placement => placement.UnitId.Equals(at))?.FindGrant(role) is { } grant)
        {
            await gate.RequireRevocableAsync([(revoked, grant.EndsAt)], at, cancellationToken).ConfigureAwait(false);
        }

        await gate.EnsureAdministratorRemainsAsync(tenantId, AdministratorLoss<TSeatId, TUnitId, TRoleId>.OfGrant(seat, at, role), cancellationToken).ConfigureAwait(false);
        holder.Revoke(at, role, gate.By);
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return holder;
    }

    /// <summary>Revokes a role a seat holds at a unit, as <see cref="RevokeAsync(TSeatId, TRoleId, TUnitId, CancellationToken)"/> does with their ids.</summary>
    /// <inheritdoc cref="RevokeAsync(TSeatId, TRoleId, TUnitId, CancellationToken)"/>
    /// <example>
    /// <code>
    /// leo = await seats.RevokeAsync(leo, surveyor, at: north, cancellationToken);
    /// </code>
    /// </example>
    public Task<TSeat> RevokeAsync(TSeat seat, TRole role, TUnit at, CancellationToken cancellationToken)
        => RevokeAsync(IdOf(seat), IdOf(role), IdOf(at), cancellationToken);

    /// <summary>
    /// Suspends a seat: its grants give it nothing until it is reactivated. Suspending takes away every grant
    /// that has not ended, so, while containment is on (<see cref="ApplicationCatalogue.ContainAccessManagingPermissions"/>),
    /// each of a role that manages access needs its permissions that do, held by the caller at that grant's unit until
    /// at least its end, as taking the role away would. A seat may suspend itself: its own grants that apply
    /// now are its own hold.
    /// </summary>
    /// <param name="seat">The seat.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The seat, suspended.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c> without <see cref="TenancyPermissions.SeatsManage"/> for the whole tenant,
    /// <c>tenancy.seat-not-found</c>, <c>tenancy.grant-exceeds-own</c>, <c>tenancy.last-admin</c>,
    /// <c>tenancy.seat-state</c>.
    /// </exception>
    /// <example>
    /// <code>
    /// var seat = await seats.SuspendAsync(command.Seat, cancellationToken);
    /// </code>
    /// </example>
    public Task<TSeat> SuspendAsync(TSeatId seat, CancellationToken cancellationToken)
        => ChangeStatusAsync(seat, (changed, by) => changed.Suspend(by), endsAdministration: true, cancellationToken);

    /// <summary>Suspends a seat, as <see cref="SuspendAsync(TSeatId, CancellationToken)"/> does with its id.</summary>
    /// <inheritdoc cref="SuspendAsync(TSeatId, CancellationToken)"/>
    /// <example>
    /// <code>
    /// tove = await seats.SuspendAsync(tove, cancellationToken);
    /// </code>
    /// </example>
    public Task<TSeat> SuspendAsync(TSeat seat, CancellationToken cancellationToken)
        => SuspendAsync(IdOf(seat), cancellationToken);

    /// <summary>
    /// Makes a suspended seat active again. Reactivating gives back every grant that has not ended, so, while
    /// containment is on (<see cref="ApplicationCatalogue.ContainAccessManagingPermissions"/>), each of a role that
    /// manages access needs its permissions that do, held by the caller at that grant's unit until at least its end,
    /// as giving the role would.
    /// </summary>
    /// <param name="seat">The seat.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The seat, active again.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c> without <see cref="TenancyPermissions.SeatsManage"/> for the whole tenant,
    /// <c>tenancy.seat-not-found</c>, <c>tenancy.grant-exceeds-own</c>, <c>tenancy.seat-state</c>.
    /// </exception>
    /// <example>
    /// <code>
    /// var seat = await seats.ReactivateAsync(command.Seat, cancellationToken);
    /// </code>
    /// </example>
    public Task<TSeat> ReactivateAsync(TSeatId seat, CancellationToken cancellationToken)
        => ChangeStatusAsync(seat, (changed, by) => changed.Reactivate(by), endsAdministration: false, cancellationToken);

    /// <summary>Makes a suspended seat active again, as <see cref="ReactivateAsync(TSeatId, CancellationToken)"/> does with its id.</summary>
    /// <inheritdoc cref="ReactivateAsync(TSeatId, CancellationToken)"/>
    /// <example>
    /// <code>
    /// tove = await seats.ReactivateAsync(tove, cancellationToken);
    /// </code>
    /// </example>
    public Task<TSeat> ReactivateAsync(TSeat seat, CancellationToken cancellationToken)
        => ReactivateAsync(IdOf(seat), cancellationToken);

    /// <summary>
    /// Deactivates a seat for good, which takes away every grant that has not ended, by the rule
    /// <see cref="SuspendAsync(TSeatId, CancellationToken)"/> follows. A seat may deactivate itself.
    /// </summary>
    /// <param name="seat">The seat.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The seat, deactivated.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c> without <see cref="TenancyPermissions.SeatsManage"/> for the whole tenant,
    /// <c>tenancy.seat-not-found</c>, <c>tenancy.grant-exceeds-own</c>, <c>tenancy.last-admin</c>,
    /// <c>tenancy.seat-state</c>.
    /// </exception>
    /// <example>
    /// <code>
    /// var seat = await seats.DeactivateAsync(command.Seat, cancellationToken);
    /// </code>
    /// </example>
    public Task<TSeat> DeactivateAsync(TSeatId seat, CancellationToken cancellationToken)
        => ChangeStatusAsync(seat, (changed, by) => changed.Deactivate(by), endsAdministration: true, cancellationToken);

    /// <summary>Deactivates a seat for good, as <see cref="DeactivateAsync(TSeatId, CancellationToken)"/> does with its id.</summary>
    /// <inheritdoc cref="DeactivateAsync(TSeatId, CancellationToken)"/>
    /// <example>
    /// <code>
    /// tove = await seats.DeactivateAsync(tove, cancellationToken);
    /// </code>
    /// </example>
    public Task<TSeat> DeactivateAsync(TSeat seat, CancellationToken cancellationToken)
        => DeactivateAsync(IdOf(seat), cancellationToken);

    // ------------------------------------------------------------------ reads

    /// <summary>
    /// Who the calling seat is, and what it may do where: its own seat, whole, as the application's class, with
    /// the tenant, the paths of the units it is placed at, the roles its grants name, and every live permission it holds
    /// now with where it is granted and every unit it reaches (<see cref="SeatOverview"/>). A seat asks this about
    /// itself; any seat of the tenant, its own included, is <see cref="OverviewAsync(TSeatId, CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    /// The seat is read as the lists read seats, untracked, and not loaded as a command loads one: nothing done to
    /// it is saved, by this question or by a save later in the same unit of work.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The calling seat's overview.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-seated</c> for every caller that is not a seat: nobody, whatever it was refused for,
    /// and system work, which has no seat of its own to show.
    /// </exception>
    /// <example>
    /// Whether to show the button that edits projects at North Coast:
    /// <code>
    /// var me = await seats.WhoAmIAsync(cancellationToken);
    /// var canEdit = me.Permissions.Any(held =&gt; held.Permission == "projects.edit" &amp;&amp; held.Reaches.Any(unit =&gt; unit.Id == northCoast));
    /// </code>
    /// </example>
    public async Task<SeatOverview> WhoAmIAsync(CancellationToken cancellationToken)
    {
        var gate = Gate();
        if (gate.User.Kind != TenancyUserKind.Seat)
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.NotSeated);
        }

        return await OverviewAsync(gate, gate.User.Tenant!.Value, gate.User.Seat!.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Any seat of the caller's tenant and what it may do where, as <see cref="WhoAmIAsync"/> answers the caller's
    /// own: the seat, whole, as the application's class, with the tenant, the paths of the units it is placed at,
    /// the roles its grants name, and every permission it holds now with where it is granted and every unit it reaches
    /// (<see cref="SeatOverview"/>). Asked about the caller's own seat, it answers what <see cref="WhoAmIAsync"/>
    /// answers.
    /// </summary>
    /// <remarks>
    /// It asks nothing of the caller beyond what every read asks, a seat of the tenant or system work in it. Who may see
    /// another person's access is the application's choice, so the request that asks this states it
    /// (<c>IRequireAccess.RequiredAccess</c>), as a request states it for everything else:
    /// <c>TenancyAccess.AtTenant(TenancyPermissions.SeatsManage)</c> for a tenant's administration, say, or
    /// <c>TenancyAccess.InTenant()</c> where the database's read rules are the rule.
    /// <para>
    /// What comes back is what the caller reads, through the store, as the lists read seats. On Postgres Tenancy's
    /// policies give a seat another seat's grants only where it may read them: at the units where it manages
    /// grants, seats or units and below them, or all of them when it manages roles for the whole tenant; and a
    /// read rule of the application's on its seat class decides which seats it reads at all. The permissions are worked
    /// out from the grants that were read, by the rule the rights are written by, since a database that keeps the
    /// rights shows a seat its own alone: a permission is shown where the caller reads the grant that gives it, and a
    /// seat that is not active holds none. Where no policy applies, on another database or in a context without
    /// row level security, every seat comes with all its grants and permissions, and the request's requirement is the
    /// only rule: choose it as the rule you mean.
    /// </para>
    /// <para>
    /// The seat is read untracked, as the lists read seats: nothing done to it is saved, by this question or by a
    /// save later in the same unit of work.
    /// </para>
    /// </remarks>
    /// <param name="seat">The seat asked about.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="Exceptions.RefusalException">
    /// The caller is nobody, with its own code; or <c>tenancy.seat-not-found</c> for a seat the caller does not
    /// read: one of another tenant, one that does not exist, or one a read rule of the application's leaves out,
    /// which the answer does not tell apart.
    /// </exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <returns>The seat's overview, as the caller reads it.</returns>
    /// <example>
    /// The units a seat is placed at, by path:
    /// <code>
    /// var overview = await seats.OverviewAsync(command.Seat, cancellationToken);
    /// var paths = overview.Seat.Placements.Select(placement =&gt; overview.UnitOf(placement.UnitId).Path);
    /// </code>
    /// </example>
    public async Task<SeatOverview> OverviewAsync(TSeatId seat, CancellationToken cancellationToken)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();

        return await OverviewAsync(gate, tenantId, seat, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Any seat of the caller's tenant and what it may do where, as <see cref="OverviewAsync(TSeatId, CancellationToken)"/> answers it by its id.</summary>
    /// <inheritdoc cref="OverviewAsync(TSeatId, CancellationToken)"/>
    /// <example>
    /// <code>
    /// var overview = await seats.OverviewAsync(leo, cancellationToken);
    /// </code>
    /// </example>
    public Task<SeatOverview> OverviewAsync(TSeat seat, CancellationToken cancellationToken)
        => OverviewAsync(IdOf(seat), cancellationToken);

    /// <summary>
    /// The tenant's seats, the application's own class, each whole and in the order of their ids: a seat has
    /// nothing of Tenancy's a person would order it by, so a screen orders what it shows by what it shows. The
    /// application selects what it shows, a name its seat class keeps, or a profile of its own found by the seat's
    /// identity, from the seats read here, with no statement more.
    /// </summary>
    /// <remarks>
    /// One statement, which reads every seat with its identity, its placements and their grants, and every field
    /// the application added; tracked by nobody, so nothing done to a seat is saved. The identity is the
    /// application's to keep: what leaves is what it selects.
    /// <para>
    /// No permission is asked, and only a database's own read rules narrow what of another seat is read: on Postgres,
    /// Tenancy's policies answer another seat's grants only where the caller may read them, and elsewhere, or in a
    /// context without row level security, every seat comes with all its placements and grants. Where another
    /// seat's grants are shown, the request that asks says who may see them, as a request that answers from
    /// <see cref="OverviewAsync(TSeatId, CancellationToken)"/> does; select a list's seats into what every member may see.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The seats the caller reads, the application's own class, in the order of their ids.</returns>
    /// <exception cref="Exceptions.RefusalException">The caller is nobody.</exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// <code>
    /// var people = (await seats.ListAsync(cancellationToken)).Select(seat =&gt; new { seat.Id, seat.DisplayName, seat.Status });
    /// </code>
    /// </example>
    public async Task<IReadOnlyList<TSeat>> ListAsync(CancellationToken cancellationToken)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();

        return InIdOrder(await store.ListSeatsAsync(tenantId, only: null, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// The seats among <paramref name="ids"/> that are in the caller's tenant, as <see cref="ListAsync"/>
    /// answers them: the application's own, whole, in the order of their ids. An id of another tenant, or of no
    /// seat, is left out without a word: the answer never says which.
    /// </summary>
    /// <remarks>
    /// Each seat comes with its placements and grants as <see cref="ListAsync"/> reads them, narrowed by the
    /// database's own read rules alone: select what the caller is shown.
    /// </remarks>
    /// <param name="ids">The seats asked about, at most <see cref="MostIds"/> different ones.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The seats found, the application's own class, in the order of their ids.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// The caller is nobody, with its own code; or <c>tenancy.too-many-ids</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// The seats of a project's crew, to show their names:
    /// <code>
    /// var crew = await seats.ByIdAsync([.. project.Crew.Select(member =&gt; member.Seat)], cancellationToken);
    /// </code>
    /// </example>
    public async Task<IReadOnlyList<TSeat>> ByIdAsync(IReadOnlyCollection<TSeatId> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var gate = Gate();
        var tenantId = gate.RequireTenant();
        var asked = TenancyReads.Asked(ids);
        if (asked.Count == 0)
        {
            return [];
        }

        return InIdOrder(await store.ListSeatsAsync(tenantId, asked, cancellationToken).ConfigureAwait(false));
    }

    // ------------------------------------------------------------------ the records it answers

    /// <summary>
    /// A seat and what it may do where: the application's own seat, whole, with only what is not on the seat itself
    /// beside it. The seat carries its id, its identity, its status, where it is placed with the roles granted there
    /// for which period, and every field the application added; Tenancy adds the tenant it is in and that tenant's
    /// name, the path of each unit it is placed at, the roles its grants name, and every permission it holds at
    /// <see cref="AsOf"/> with where that permission reaches. <see cref="WhoAmIAsync"/> answers it for the calling seat,
    /// and <see cref="OverviewAsync(TSeatId, CancellationToken)"/> for any seat of the tenant.
    /// <para>
    /// Nothing of it is the seat a second time, so an application shows the seat by what it chose, a name its seat
    /// class keeps or a profile found by the identity, from the seat itself, and puts its placements beside the paths
    /// and roles with <see cref="UnitOf"/> and <see cref="RoleOf"/>. What leaves is what the application selects:
    /// the seat has its identity.
    /// </para>
    /// <para>
    /// The seat and the roles were read for the overview and are tracked by nobody: nothing done to them is saved, by
    /// this question or by a save later in the same unit of work. The tenant is loaded as a command loads it. Nothing in
    /// the record is the caller's own but the question that made it, so it shows any seat the same way. What it holds
    /// of a seat is what the caller reads: where a database's policies give the caller only some of another seat's
    /// grants, as Tenancy's do on Postgres, the seat comes with those, and the roles and permissions are those that
    /// they name and give.
    /// </para>
    /// </summary>
    /// <param name="Tenant">The tenant the seat is in, the application's own class.</param>
    /// <param name="TenantName">The tenant's name, which is its organization's.</param>
    /// <param name="Seat">The seat, the application's own class, with its placements and the grants the caller reads.</param>
    /// <param name="Units">Each unit the seat is placed at, once, by its path from the root.</param>
    /// <param name="Roles">
    /// Each role the seat's grants name, once, the application's own class, by name, whether or not the grant applies
    /// now; a role the store does not answer, one a filter of the application's own hides, is left out rather than
    /// made up.
    /// </param>
    /// <param name="Permissions">
    /// Every permission the seat holds at <paramref name="AsOf"/> from those grants, where it is granted and every unit it
    /// reaches: none while the seat is not active.
    /// </param>
    /// <param name="AsOf">
    /// The moment the overview holds for: the permissions are those held then, and a grant of the seat applies then when
    /// <c>grant.AppliesAt(AsOf)</c>.
    /// </param>
    public sealed record SeatOverview(
        TTenant Tenant,
        string TenantName,
        TSeat Seat,
        IReadOnlyList<UnitRef> Units,
        IReadOnlyList<TRole> Roles,
        IReadOnlyList<PermissionReach> Permissions,
        DateTimeOffset AsOf)
    {
        /// <summary>A unit the seat is placed at, by its path: what <see cref="Units"/> holds for it.</summary>
        /// <param name="unit">The unit of one of the seat's placements.</param>
        /// <exception cref="KeyNotFoundException">The seat is placed at no such unit.</exception>
        public UnitRef UnitOf(TUnitId unit)
            => Units.FirstOrDefault(found => found.Id.Equals(unit))
               ?? throw new KeyNotFoundException($"The seat is placed at no unit {unit}: an overview names the units of the seat's own placements.");

        /// <summary>A role the seat's grants name: what <see cref="Roles"/> holds for it.</summary>
        /// <param name="role">The role of one of the seat's grants.</param>
        /// <returns>
        /// The role; or <see langword="null"/> when a grant of the seat names it and the store answered no such role,
        /// as a filter of the application's own on its role class does, a soft delete say. The grant is the seat's
        /// own all the same, so a screen shows it with what the application chooses for a role it cannot name.
        /// </returns>
        /// <exception cref="KeyNotFoundException">No grant of the seat names such a role.</exception>
        public TRole? RoleOf(TRoleId role)
            => Roles.FirstOrDefault(found => found.Id.Equals(role))
               ?? (Seat.Placements.Any(placement => placement.Grants.Any(grant => grant.RoleId.Equals(role)))
                   ? null
                   : throw new KeyNotFoundException($"No grant of the seat names a role {role}: an overview names the roles of the seat's own grants."));
    }

    /// <summary>
    /// A permission a seat holds now: the units it is granted at, every unit it reaches (those and every unit below
    /// them), and whether it holds for the whole tenant, which is being granted at the root.
    /// </summary>
    /// <param name="Permission">The permission.</param>
    /// <param name="AtTenant">Whether the seat holds it for the whole tenant: one of its grants is at the root.</param>
    /// <param name="GrantedAt">The units a grant of the seat gives it at, each once, by its path from the root.</param>
    /// <param name="Reaches">Every unit it holds the permission at: those, and every unit below them, each once, by path.</param>
    public sealed record PermissionReach(string Permission, bool AtTenant, IReadOnlyList<UnitRef> GrantedAt, IReadOnlyList<UnitRef> Reaches);

    /// <summary>
    /// A unit, with its path from the root, such as <c>Harbor Works / North / North Coast</c>: the units an overview
    /// names, whichever of them the caller reads whole.
    /// </summary>
    /// <param name="Id">The unit.</param>
    /// <param name="Path">The names of the unit and the units above it, the root first, joined with <c>" / "</c>.</param>
    public sealed record UnitRef(TUnitId Id, string Path);

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// The overview of <paramref name="seatId"/> in <paramref name="tenantId"/>, the caller's tenant, as the caller
    /// reads it: <see cref="WhoAmIAsync"/> and <see cref="OverviewAsync(TSeatId, CancellationToken)"/> answer from here,
    /// so a seat's own overview and another's are made the same way.
    /// </summary>
    /// <exception cref="Exceptions.RefusalException"><c>tenancy.seat-not-found</c>: the caller reads no such seat.</exception>
    private async Task<SeatOverview> OverviewAsync(
        TenancyGate<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId> gate,
        TTenantId tenantId,
        TSeatId seatId,
        CancellationToken cancellationToken)
    {
        var now = gate.Now;

        // The seat first: a seat the caller does not read is refused before anything else is read.
        var seat = (await store.ListSeatsAsync(tenantId, [seatId], cancellationToken).ConfigureAwait(false)).FirstOrDefault()
                   ?? throw TenancyRefusals.Refuse(TenancyRefusals.SeatNotFound);
        var tenant = await gate.LoadTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var organization = await gate.OrganizationTheUserReadsAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var roles = (await store.ListRolesAsync(tenantId, cancellationToken).ConfigureAwait(false)).ToDictionary(role => role.Id);
        var units = organization is null
            ? await UnitsNamedOnlyAsync(tenantId, seat, cancellationToken).ConfigureAwait(false)
            : UnitsOf(await TenancyUnitMap<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.ReadAsync(store, organization, cancellationToken).ConfigureAwait(false));

        // What a role grants, from the rows the access questions read: a filter of the application's own on its
        // role class hides a role's name, never what its grants give, as the rights the questions decide by are
        // written from these same rows. A tenant has few roles, so they are one statement.
        var facts = (await store.Queries.ListAsync(store.Reads.Roles.Where(role => role.TenantId.Equals(tenantId)), cancellationToken).ConfigureAwait(false))
            .ToDictionary(role => role.Id, role => new RoleFacts(role.Status == RoleStatus.Active, role.Permissions));

        // Only what is not on the seat: the paths of its placements' units and the roles its grants name. A role
        // the store does not answer, one a filter of the application's own hides, is left out rather than made
        // up, and SeatOverview.RoleOf answers null for it.
        var placedAt = units.Refs([.. seat.Placements.Select(placement => placement.UnitId)]);
        var named = seat.Placements
            .SelectMany(placement => placement.Grants.Select(grant => grant.RoleId))
            .Distinct()
            .Select(role => roles.GetValueOrDefault(role))
            .OfType<TRole>()
            .OrderBy(role => role.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // The permissions the grants that were read give now, worked out as the rights are written: by an active seat,
        // from an active role, and only the permissions the catalogue keeps live, so a permission retired since the rights were
        // written holds nowhere. Not read from the rights themselves, which a database that keeps them shows a seat
        // only of its own: worked out from the grants, a permission of another seat's is shown exactly where the caller
        // reads the grant that gives it.
        var held = TenancyProjection.RightsOf(tenantId, seat.Id, seat.Status, TenancyProjection.GrantsOf(seat), role => facts.GetValueOrDefault(role), catalogue)
            .Where(right => right.StartsAt <= now && (right.EndsAt == null || right.EndsAt > now));
        var permissions = held
            .GroupBy(right => right.Permission, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var grantedAt = group.Select(right => right.UnitId).Distinct().ToArray();
                return new PermissionReach(
                    group.Key,
                    units.Root is { } root && grantedAt.Contains(root),
                    units.Refs(grantedAt),
                    units.Refs([.. grantedAt.SelectMany(units.Subtree)]));
            })
            .ToArray();

        // The organization names the tenant; for a seat a restrictive rule keeps out of it, Tenancy's function names
        // it as it names the tenants in the picker.
        var name = organization?.Name
                   ?? await store.TenantNameAsync(tenantId, cancellationToken).ConfigureAwait(false)
                   ?? tenant.Slug.Value;

        return new SeatOverview(tenant, name, seat, placedAt, named, permissions, now);
    }

    /// <summary>The units an overview names, from the organization the caller reads: every unit, with the tree.</summary>
    private static OverviewUnits UnitsOf(TenancyUnitMap<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId> map)
        => new(map.Root, map.Refs, map.Subtree);

    /// <summary>
    /// The units an overview names, for a seat that a restrictive rule of the application's keeps out of the
    /// organization: the units it is placed at, named through the storage as <c>OrganizationManager.UnitsByIdAsync</c>
    /// names them, and nothing below them, which the seat does not read.
    /// </summary>
    private async Task<OverviewUnits> UnitsNamedOnlyAsync(TTenantId tenantId, TSeat seat, CancellationToken cancellationToken)
    {
        TUnitId[] placedAt = [.. seat.Placements.Select(placement => placement.UnitId).Distinct()];
        var named = placedAt.Length == 0
            ? []
            : await store.UnitsNamedAsync(tenantId, placedAt, cancellationToken).ConfigureAwait(false) ?? [];
        var paths = named.ToDictionary(unit => unit.Id, unit => unit.Path);

        return new OverviewUnits(
            named.FirstOrDefault(unit => unit.Depth == 1)?.Id,
            ids => [.. ids.Distinct().Where(paths.ContainsKey).Select(id => new UnitRef(id, paths[id])).OrderBy(unit => unit.Path, StringComparer.OrdinalIgnoreCase)],
            id => [id]);
    }

    /// <summary>What an overview needs of the units it names: the root, the path of each, and what is below one.</summary>
    private sealed record OverviewUnits(TUnitId? Root, Func<IEnumerable<TUnitId>, IReadOnlyList<UnitRef>> Refs, Func<TUnitId, IEnumerable<TUnitId>> Subtree);

    private async Task<TSeat> ChangeStatusAsync(TSeatId seat, Action<TSeat, TenancyActor<TSeatId>?> change, bool endsAdministration, CancellationToken cancellationToken)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();
        await gate.SerializeAsync(tenantId, cancellationToken).ConfigureAwait(false);
        await gate.RequireAtTenantAsync(TenancyPermissions.SeatsManage, cancellationToken).ConfigureAwait(false);

        var changed = await gate.LoadSeatAsync(seat, cancellationToken).ConfigureAwait(false);
        await gate.RequireStatusChangeableAsync(changed, cancellationToken).ConfigureAwait(false);
        if (endsAdministration)
        {
            await gate.EnsureAdministratorRemainsAsync(tenantId, AdministratorLoss<TSeatId, TUnitId, TRoleId>.OfSeat(seat), cancellationToken).ConfigureAwait(false);
        }

        change(changed, gate.By);
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return changed;
    }

    /// <summary>What every command asks before it acts, for one call: the caller and "now" are fixed when it starts.</summary>
    private TenancyGate<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId> Gate() => new(store, catalogue, clock);

    /// <summary>
    /// The seats in the order of their ids, so the order is the same every time: a seat has nothing of Tenancy's that a
    /// person would order it by.
    /// </summary>
    private static TSeat[] InIdOrder(IEnumerable<TSeat> seats) => [.. seats.OrderBy(seat => seat.Id, IdOrder)];

    private static TSeatId IdOf(TSeat seat) => (seat ?? throw new ArgumentNullException(nameof(seat))).Id;

    private static TUnitId IdOf(TUnit unit) => (unit ?? throw new ArgumentNullException(nameof(unit))).Id;

    private static TRoleId IdOf(TRole role) => (role ?? throw new ArgumentNullException(nameof(role))).Id;
}
