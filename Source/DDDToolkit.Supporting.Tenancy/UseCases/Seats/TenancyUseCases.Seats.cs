using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>
    /// People's seats: adding them, placing them in units, granting them roles there, and suspending,
    /// reactivating or deactivating them.
    /// <para>
    /// Holding <see cref="TenancyKeys.GrantsManage"/> at a unit is enough to give roles there. Its holder
    /// gives any active role of the tenant that manages no access to a seat placed there or below, and takes any
    /// such role away, without holding the role's keys: to another seat for as long as it says, to itself for no
    /// longer than it holds <see cref="TenancyKeys.GrantsManage"/> there. A role that manages access (see
    /// <see cref="TenancyCatalogue.AccessManagingKeysOf"/>) stays contained: a seat gives it, and takes it
    /// away, only while holding there each of its keys that manage access, for at least as long as the grant
    /// runs, and never gives it to itself. Withdrawing a placement takes its grants away, by the same rule, and
    /// so does suspending or deactivating a seat, while reactivating one gives its grants back by that rule too.
    /// System work in a tenant is not held to any of that. When the application turns containment off
    /// (<see cref="ApplicationCatalogue.ContainAccessManagingKeys"/>), a role that manages access goes as one
    /// that manages none: a grants manager gives every role, to another seat for as long as it says and to itself
    /// for no longer than it holds <see cref="TenancyKeys.GrantsManage"/> there, and the key a command asks where
    /// it acts is all a seat's status takes. Nothing leaves a tenant that has an administrator
    /// without one, whoever asks, and that is decided before the seat changes, so a refused command leaves
    /// nothing for a later save to write. A seat of another tenant is not found.
    /// </para>
    /// <para>
    /// Nothing here names a seat: a seat has no name in Tenancy. What the application keeps on its seat class, a
    /// name it is shown by among it, it sets in the callback of <see cref="AddSeatAsync"/>, and changes with a use
    /// case of its own, under a rule of its own.
    /// </para>
    /// </summary>
    /// <param name="store">Where seats and roles are loaded and saved.</param>
    /// <param name="catalogue">The keys asked for, and which keys are live.</param>
    /// <param name="clock">What "now" is.</param>
    public sealed class SeatCommands(
        IStore store,
        TenancyCatalogue catalogue,
        TimeProvider clock)
    {
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
        /// <returns>The new seat's id.</returns>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.not-permitted</c> without <see cref="TenancyKeys.SeatsManage"/> for the whole tenant,
        /// <c>tenancy.identity-has-seat</c>, <c>tenancy.identity-required</c>.
        /// </exception>
        public async Task<TSeatId> AddSeatAsync(Guid identity, CancellationToken cancellationToken, TSeatId? id = null, Action<TSeat>? configure = null)
        {
            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireTenant();
            await gate.RequireTenantWideAsync(TenancyKeys.SeatsManage, cancellationToken).ConfigureAwait(false);

            if (await store.IdentityHasSeatAsync(tenantId, identity, cancellationToken).ConfigureAwait(false))
            {
                throw TenancyRefusals.Of(TenancyRefusals.IdentityHasSeat);
            }

            var seat = TenancyInstances.NewSeat<TSeat, TSeatId, TTenantId, TUnitId, TRoleId>(
                id ?? TSeatId.Create(), tenantId, identity, gate.By);

            // The application's own fields, before the store has the seat: a callback that throws leaves nothing
            // for this save or a later one to write, and what it sets is written with the seat.
            configure?.Invoke(seat);
            store.Add(seat);
            await store.SaveAsync(cancellationToken).ConfigureAwait(false);
            return seat.Id;
        }

        /// <summary>Places a seat in an active unit.</summary>
        /// <param name="seat">The seat.</param>
        /// <param name="unit">The unit.</param>
        /// <param name="primary">Whether this becomes the seat's primary placement.</param>
        /// <param name="cancellationToken">Cancels the work.</param>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.not-permitted</c> without <see cref="TenancyKeys.SeatsManage"/> at the unit,
        /// <c>tenancy.seat-not-found</c>, <c>tenancy.unit-not-found</c>, <c>tenancy.unit-not-active</c>, and what
        /// the seat refuses: placing itself, a second placement in the unit, a second primary.
        /// </exception>
        public async Task PlaceAsync(TSeatId seat, TUnitId unit, bool primary, CancellationToken cancellationToken)
        {
            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireTenant();
            await gate.SerializeAsync(tenantId, cancellationToken).ConfigureAwait(false);
            await gate.RequireAtAsync(TenancyKeys.SeatsManage, unit, cancellationToken).ConfigureAwait(false);

            var placed = await gate.LoadSeatAsync(seat, cancellationToken).ConfigureAwait(false);
            await RequireActiveUnitAsync(gate, tenantId, unit, cancellationToken).ConfigureAwait(false);

            placed.Place(unit, primary, gate.Now, gate.ActorFor(seat), gate.By);
            await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Withdraws a seat from a unit, revoking every role it holds there. When it holds any, the caller needs
        /// <see cref="TenancyKeys.GrantsManage"/> there too, and, while containment is on
        /// (<see cref="ApplicationCatalogue.ContainAccessManagingKeys"/>), for each of those roles that manages
        /// access, its keys that do, held there until at least that grant's end: taking a placement away takes its
        /// grants away. A role that manages no access needs none of its keys.
        /// </summary>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.not-permitted</c>, <c>tenancy.seat-not-found</c>, <c>tenancy.placement-not-found</c>,
        /// <c>tenancy.grant-exceeds-own</c>, <c>tenancy.last-admin</c>.
        /// </exception>
        public async Task WithdrawAsync(TSeatId seat, TUnitId unit, CancellationToken cancellationToken)
        {
            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireTenant();
            await gate.SerializeAsync(tenantId, cancellationToken).ConfigureAwait(false);
            await gate.RequireAtAsync(TenancyKeys.SeatsManage, unit, cancellationToken).ConfigureAwait(false);

            var withdrawn = await gate.LoadSeatAsync(seat, cancellationToken).ConfigureAwait(false);
            var placement = withdrawn.Placements.FirstOrDefault(candidate => candidate.UnitId.Equals(unit))
                ?? throw TenancyRefusals.Of(TenancyRefusals.PlacementNotFound);

            if (placement.Grants.Count > 0)
            {
                await gate.RequireAtAsync(TenancyKeys.GrantsManage, unit, cancellationToken).ConfigureAwait(false);

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

            await gate.EnsureAdministratorRemainsAsync(tenantId, AdministratorLoss.OfPlacement(seat, unit), cancellationToken).ConfigureAwait(false);
            withdrawn.Withdraw(unit, gate.By);
            await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Makes a seat's placement in a unit its primary one.</summary>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.not-permitted</c> without <see cref="TenancyKeys.SeatsManage"/> at the unit,
        /// <c>tenancy.seat-not-found</c>, <c>tenancy.placement-not-found</c>.
        /// </exception>
        public async Task MakePrimaryAsync(TSeatId seat, TUnitId unit, CancellationToken cancellationToken)
        {
            var gate = new Gate(store, catalogue, clock);
            gate.RequireTenant();
            await gate.RequireAtAsync(TenancyKeys.SeatsManage, unit, cancellationToken).ConfigureAwait(false);

            var changed = await gate.LoadSeatAsync(seat, cancellationToken).ConfigureAwait(false);
            changed.MakePrimary(unit, gate.By);
            await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Grants a seat a role at an active unit where it is placed, from now until <paramref name="until"/>.
        /// With <see cref="TenancyKeys.GrantsManage"/> at the unit, a seat gives another seat any role that
        /// manages no access, for any period, and itself one for no longer than it holds
        /// <see cref="TenancyKeys.GrantsManage"/> there. While containment is on
        /// (<see cref="ApplicationCatalogue.ContainAccessManagingKeys"/>), a role that manages access it gives only
        /// while it holds there each of the role's keys that manage access, for at least as long as the grant
        /// lasts, and never to itself: a seat whose own grant ends next week does not give such a role for good.
        /// Off, such a role goes as any other.
        /// </summary>
        /// <param name="seat">The seat.</param>
        /// <param name="unit">The unit of one of its placements.</param>
        /// <param name="role">The role, which must be active.</param>
        /// <param name="until">When the grant ends, or <see langword="null"/> for no end.</param>
        /// <param name="reason">Why, or <see langword="null"/>.</param>
        /// <param name="cancellationToken">Cancels the work.</param>
        /// <param name="from">
        /// When the grant starts, for imports and seeding: system work only. A grant a seat makes starts now,
        /// and a seat that passes this is refused rather than silently ignored.
        /// </param>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.start-system-only</c>, <c>tenancy.not-permitted</c> without <see cref="TenancyKeys.GrantsManage"/>
        /// at the unit, <c>tenancy.role-not-found</c>, <c>tenancy.self-appointment</c> for a role that manages
        /// access given to the caller itself while containment is on, <c>tenancy.grant-exceeds-own</c>,
        /// <c>tenancy.seat-not-found</c>,
        /// <c>tenancy.unit-not-found</c>, <c>tenancy.unit-not-active</c> for an archived unit,
        /// <c>tenancy.invalid-period</c>, and what the seat refuses.
        /// </exception>
        public async Task GrantAsync(
            TSeatId seat,
            TUnitId unit,
            TRoleId role,
            DateTimeOffset? until,
            string? reason,
            CancellationToken cancellationToken,
            DateTimeOffset? from = null)
        {
            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireTenant();
            if (from is not null && !gate.BySystem)
            {
                throw TenancyRefusals.Of(TenancyRefusals.StartSystemOnly);
            }

            await gate.SerializeAsync(tenantId, cancellationToken).ConfigureAwait(false);
            await gate.RequireAtAsync(TenancyKeys.GrantsManage, unit, cancellationToken).ConfigureAwait(false);

            var granted = await gate.LoadRoleAsync(role, cancellationToken).ConfigureAwait(false);
            await gate.RequireGrantableAsync(seat, unit, granted, until, cancellationToken).ConfigureAwait(false);

            var holder = await gate.LoadSeatAsync(seat, cancellationToken).ConfigureAwait(false);
            await RequireActiveUnitAsync(gate, tenantId, unit, cancellationToken).ConfigureAwait(false);
            var period = GrantPeriod.Between(from ?? gate.Now, until);
            holder.Grant(unit, role, granted.Facts, period, gate.ActorFor(seat), reason, gate.By);
            await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Revokes a role a seat holds at a unit. With <see cref="TenancyKeys.GrantsManage"/> at the unit, a seat
        /// takes away any role that manages no access, an archived role included, which manages nothing. While
        /// containment is on (<see cref="ApplicationCatalogue.ContainAccessManagingKeys"/>), a role that manages
        /// access it takes away only while it holds there each of the role's keys that manage access, until at
        /// least the grant's end, or for good for a grant with none; off, such a role goes as any other. A seat's
        /// own grant that applies now is its own hold, so a seat may take away its current roles.
        /// </summary>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.not-permitted</c> without <see cref="TenancyKeys.GrantsManage"/> at the unit,
        /// <c>tenancy.role-not-found</c>, <c>tenancy.seat-not-found</c>, <c>tenancy.grant-exceeds-own</c>,
        /// <c>tenancy.last-admin</c>, <c>tenancy.grant-not-found</c>.
        /// </exception>
        public async Task RevokeAsync(TSeatId seat, TUnitId unit, TRoleId role, CancellationToken cancellationToken)
        {
            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireTenant();
            await gate.SerializeAsync(tenantId, cancellationToken).ConfigureAwait(false);
            await gate.RequireAtAsync(TenancyKeys.GrantsManage, unit, cancellationToken).ConfigureAwait(false);

            var revoked = await gate.LoadRoleAsync(role, cancellationToken).ConfigureAwait(false);
            var holder = await gate.LoadSeatAsync(seat, cancellationToken).ConfigureAwait(false);

            // How long the grant runs is read from the seat. A missing grant is the seat's to refuse.
            if (holder.Placements.FirstOrDefault(placement => placement.UnitId.Equals(unit))?.FindGrant(role) is { } grant)
            {
                await gate.RequireRevocableAsync([(revoked, grant.EndsAt)], unit, cancellationToken).ConfigureAwait(false);
            }

            await gate.EnsureAdministratorRemainsAsync(tenantId, AdministratorLoss.OfGrant(seat, unit, role), cancellationToken).ConfigureAwait(false);
            holder.Revoke(unit, role, gate.By);
            await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Suspends a seat: its grants give it nothing until it is reactivated. Suspending takes away every grant
        /// that has not ended, so, while containment is on (<see cref="ApplicationCatalogue.ContainAccessManagingKeys"/>),
        /// each of a role that manages access needs its keys that do, held by the caller at that grant's unit until
        /// at least its end, as taking the role away would. A seat may suspend itself: its own grants that apply
        /// now are its own hold.
        /// </summary>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.not-permitted</c> without <see cref="TenancyKeys.SeatsManage"/> for the whole tenant,
        /// <c>tenancy.seat-not-found</c>, <c>tenancy.grant-exceeds-own</c>, <c>tenancy.last-admin</c>,
        /// <c>tenancy.seat-state</c>.
        /// </exception>
        public Task SuspendAsync(TSeatId seat, CancellationToken cancellationToken)
            => ChangeStatusAsync(seat, (changed, by) => changed.Suspend(by), endsAdministration: true, cancellationToken);

        /// <summary>
        /// Makes a suspended seat active again. Reactivating gives back every grant that has not ended, so, while
        /// containment is on (<see cref="ApplicationCatalogue.ContainAccessManagingKeys"/>), each of a role that
        /// manages access needs its keys that do, held by the caller at that grant's unit until at least its end,
        /// as giving the role would.
        /// </summary>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.not-permitted</c> without <see cref="TenancyKeys.SeatsManage"/> for the whole tenant,
        /// <c>tenancy.seat-not-found</c>, <c>tenancy.grant-exceeds-own</c>, <c>tenancy.seat-state</c>.
        /// </exception>
        public Task ReactivateAsync(TSeatId seat, CancellationToken cancellationToken)
            => ChangeStatusAsync(seat, (changed, by) => changed.Reactivate(by), endsAdministration: false, cancellationToken);

        /// <summary>
        /// Deactivates a seat for good, which takes away every grant that has not ended, by the rule
        /// <see cref="SuspendAsync"/> follows. A seat may deactivate itself.
        /// </summary>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.not-permitted</c> without <see cref="TenancyKeys.SeatsManage"/> for the whole tenant,
        /// <c>tenancy.seat-not-found</c>, <c>tenancy.grant-exceeds-own</c>, <c>tenancy.last-admin</c>,
        /// <c>tenancy.seat-state</c>.
        /// </exception>
        public Task DeactivateAsync(TSeatId seat, CancellationToken cancellationToken)
            => ChangeStatusAsync(seat, (changed, by) => changed.Deactivate(by), endsAdministration: true, cancellationToken);

        private async Task ChangeStatusAsync(TSeatId seat, Action<TSeat, TenancyActor<TSeatId>?> change, bool endsAdministration, CancellationToken cancellationToken)
        {
            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireTenant();
            await gate.SerializeAsync(tenantId, cancellationToken).ConfigureAwait(false);
            await gate.RequireTenantWideAsync(TenancyKeys.SeatsManage, cancellationToken).ConfigureAwait(false);

            var changed = await gate.LoadSeatAsync(seat, cancellationToken).ConfigureAwait(false);
            await gate.RequireStatusChangeableAsync(changed, cancellationToken).ConfigureAwait(false);
            if (endsAdministration)
            {
                await gate.EnsureAdministratorRemainsAsync(tenantId, AdministratorLoss.OfSeat(seat), cancellationToken).ConfigureAwait(false);
            }

            change(changed, gate.By);
            await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Nothing new goes to an archived unit: no placement, no grant and no invitation. What is already there keeps
    /// working.
    /// </summary>
    private static async Task RequireActiveUnitAsync(Gate gate, TTenantId tenant, TUnitId unit, CancellationToken cancellationToken)
    {
        var organization = await gate.LoadOrganizationAsync(tenant, cancellationToken).ConfigureAwait(false);
        var target = organization.FindUnit(unit) ?? throw TenancyRefusals.Of(TenancyRefusals.UnitNotFound);
        if (target.Status != UnitStatus.Active)
        {
            throw TenancyRefusals.Of(TenancyRefusals.UnitNotActive, ("Unit", unit));
        }
    }
}
