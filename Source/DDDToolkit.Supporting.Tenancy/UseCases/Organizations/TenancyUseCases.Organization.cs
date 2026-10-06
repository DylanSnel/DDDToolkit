using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>
    /// The organization's tree: adding, renaming, moving and archiving units. Each needs
    /// <see cref="TenancyKeys.UnitsManage"/> where the change is made, so a seat that manages a region manages
    /// the units below it and nothing beside or above it.
    /// <para>
    /// Two edits of the tree at the same time are kept apart by the organization's version. A move also
    /// changes what every key held above the moved unit reaches, so it takes the access revision too.
    /// </para>
    /// </summary>
    /// <param name="store">Where the organization is loaded and saved.</param>
    /// <param name="catalogue">The keys asked for.</param>
    /// <param name="clock">What "now" is.</param>
    public sealed class OrganizationCommands(
        IStore store,
        TenancyCatalogue catalogue,
        TimeProvider clock)
    {
        /// <summary>Adds a unit below <paramref name="parent"/>.</summary>
        /// <param name="parent">The unit it hangs under.</param>
        /// <param name="name">Its name.</param>
        /// <param name="cancellationToken">Cancels the work.</param>
        /// <param name="id">Its id, for imports and seeding; a new one otherwise.</param>
        /// <param name="configure">
        /// Sets the fields the application added to its unit class, such as what kind of unit it is, on the new
        /// unit once the caller and the tree are checked and before the organization takes the unit in, so they are
        /// saved in the same transaction and the class's own rules judge them there. When it throws, the unit is not
        /// added: nothing is saved, by this call or by a later save in the same scope.
        /// </param>
        /// <returns>The new unit's id.</returns>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.not-permitted</c> without <see cref="TenancyKeys.UnitsManage"/> at the parent, and what the
        /// organization refuses, <c>tenancy.flat-tenant</c> first.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="id"/> is already a unit's: a mistake in the import or seeding that gave it, not a
        /// refusal.
        /// </exception>
        public async Task<TUnitId> AddUnitAsync(
            TUnitId parent,
            string name,
            CancellationToken cancellationToken,
            TUnitId? id = null,
            Action<TUnit>? configure = null)
        {
            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireTenant();
            await gate.RequireAtAsync(TenancyKeys.UnitsManage, parent, cancellationToken).ConfigureAwait(false);

            var tenant = await gate.LoadTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
            var organization = await gate.LoadOrganizationAsync(tenantId, cancellationToken).ConfigureAwait(false);
            // The organization runs the callback before it takes the unit in: one that throws leaves the tracked
            // organization as it was, so no later save in this scope writes the unit without the application's
            // fields. What it sets is written with the unit.
            var unit = organization.AddUnit(id ?? TUnitId.Create(), parent, name, tenant.Shape, gate.By, configure);
            await store.SaveAsync(cancellationToken).ConfigureAwait(false);
            return unit.Id;
        }

        /// <summary>Renames a unit.</summary>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.not-permitted</c> without <see cref="TenancyKeys.UnitsManage"/> at the unit, and what the
        /// organization refuses.
        /// </exception>
        public async Task RenameUnitAsync(TUnitId unit, string name, CancellationToken cancellationToken)
        {
            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireTenant();
            await gate.RequireAtAsync(TenancyKeys.UnitsManage, unit, cancellationToken).ConfigureAwait(false);

            var organization = await gate.LoadOrganizationAsync(tenantId, cancellationToken).ConfigureAwait(false);
            organization.RenameUnit(unit, name, gate.By);
            await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Moves a unit, and everything below it, under another unit. The caller needs the key where the unit
        /// hangs now and where it goes, so a unit only moves between parts of the tree the caller manages both of.
        /// A move changes which grants reach the unit, so a seat moves it only as far as it could give and take
        /// away what that changes: the move gives the seat itself no key it does not hold at the unit already,
        /// and none for longer than it holds it there; and it gives or takes away no key that manages access,
        /// from anyone, that the seat does not hold at the unit for at least as long as that grant runs.
        /// </summary>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.unit-not-found</c>, <c>tenancy.not-permitted</c> without <see cref="TenancyKeys.UnitsManage"/>
        /// at the current parent and at the new one, what the organization refuses, and
        /// <c>tenancy.grant-exceeds-own</c> naming the keys the move would give or take away that the calling seat
        /// lacks at the unit, or lacks for long enough.
        /// </exception>
        public async Task MoveUnitAsync(TUnitId unit, TUnitId newParent, CancellationToken cancellationToken)
        {
            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireTenant();
            await gate.SerializeAsync(tenantId, cancellationToken).ConfigureAwait(false);

            var organization = await gate.LoadOrganizationAsync(tenantId, cancellationToken).ConfigureAwait(false);
            var moved = organization.FindUnit(unit) ?? throw TenancyRefusals.Of(TenancyRefusals.UnitNotFound);
            await gate.RequireAtAsync(TenancyKeys.UnitsManage, moved.ParentId ?? unit, cancellationToken).ConfigureAwait(false);
            await gate.RequireAtAsync(TenancyKeys.UnitsManage, newParent, cancellationToken).ConfigureAwait(false);

            organization.RequireMove(unit, newParent);
            await gate.RequireMovableAsync(unit, moved.ParentId!.Value, newParent, cancellationToken).ConfigureAwait(false);
            organization.MoveUnit(unit, newParent, gate.By);
            await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Archives a unit. The caller needs the key at its parent: archiving takes the unit out of the part of
        /// the tree above it. Seats placed there and rights held there keep working.
        /// </summary>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.unit-not-found</c>, <c>tenancy.not-permitted</c> without <see cref="TenancyKeys.UnitsManage"/>
        /// at the parent, and what the organization refuses.
        /// </exception>
        public async Task ArchiveUnitAsync(TUnitId unit, CancellationToken cancellationToken)
        {
            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireTenant();

            // A placement or a grant at the unit checks it is active without changing the organization, so the
            // organization's version does not stop one saved next to this archive; the revision does.
            await gate.SerializeAsync(tenantId, cancellationToken).ConfigureAwait(false);

            var organization = await gate.LoadOrganizationAsync(tenantId, cancellationToken).ConfigureAwait(false);
            var archived = organization.FindUnit(unit) ?? throw TenancyRefusals.Of(TenancyRefusals.UnitNotFound);
            await gate.RequireAtAsync(TenancyKeys.UnitsManage, archived.ParentId ?? unit, cancellationToken).ConfigureAwait(false);

            organization.ArchiveUnit(unit, gate.By);
            await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
