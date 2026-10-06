using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>
    /// The tenant's roles: making them, renaming them, changing their keys and archiving them. Each needs
    /// <see cref="TenancyKeys.RolesManage"/> for the whole tenant.
    /// <para>
    /// A holder of that key may put any live key into any role, keys it does not hold included. That is on
    /// purpose: it is the administrator's key, and an administrator can give themselves anything already. One
    /// exception: an administrator alone, holding it at the root with no end, changes which keys that manage
    /// access a role holds, by adding one, taking one out or archiving a role that holds one. A role that gains
    /// one gives it to every seat that holds the role, seats that gave themselves the role included, and a role
    /// that loses one takes it from every holder at once, so someone holding the key for a week neither gives
    /// lasting power over access that way nor takes away what it could not revoke grant by grant. A change of a
    /// role's keys reaches every seat that holds the role at the next save. Whether a change leaves the tenant
    /// an administrator is decided before the role changes, so a refused command leaves nothing for a later save
    /// to write.
    /// </para>
    /// </summary>
    /// <param name="store">Where roles are loaded and saved.</param>
    /// <param name="catalogue">The keys a role may hold.</param>
    /// <param name="options">How new ids are made.</param>
    /// <param name="clock">What "now" is.</param>
    public sealed class RoleCommands(
        IStore store,
        TenancyCatalogue catalogue,
        TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId> options,
        TimeProvider clock)
    {
        /// <summary>Makes a role by hand; it comes from no pack.</summary>
        /// <param name="name">Its name, unique in the tenant, ignoring case.</param>
        /// <param name="description">What it is for.</param>
        /// <param name="keys">The keys it grants; the keys they imply are added.</param>
        /// <param name="cancellationToken">Cancels the work.</param>
        /// <param name="id">
        /// Its id, for imports and seeding; a new one otherwise. An id that is already a role's is a mistake in
        /// the import, which the save reports.
        /// </param>
        /// <returns>The new role's id.</returns>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.not-permitted</c> without <see cref="TenancyKeys.RolesManage"/> for the whole tenant,
        /// <c>tenancy.role-name-taken</c>, <c>tenancy.name-invalid</c>, <c>tenancy.unknown-permission</c>.
        /// </exception>
        public async Task<TRoleId> CreateAsync(
            string name,
            string description,
            IReadOnlyCollection<string> keys,
            CancellationToken cancellationToken,
            TRoleId? id = null)
        {
            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireTenant();
            await gate.RequireTenantWideAsync(TenancyKeys.RolesManage, cancellationToken).ConfigureAwait(false);
            await RequireNameFreeAsync(tenantId, name, except: null, cancellationToken).ConfigureAwait(false);

            var role = TenancyInstances.NewRole<TRole, TRoleId, TTenantId, TSeatId>(
                id ?? options.Checked().NewRoleId!(), tenantId, new RoleDraft(name, description, keys), catalogue, gate.By);
            store.Add(role);
            await store.SaveAsync(cancellationToken).ConfigureAwait(false);
            return role.Id;
        }

        /// <summary>Renames a role and describes it again.</summary>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.not-permitted</c> without <see cref="TenancyKeys.RolesManage"/> for the whole tenant,
        /// <c>tenancy.role-not-found</c>, <c>tenancy.role-name-taken</c>, <c>tenancy.role-archived</c>,
        /// <c>tenancy.name-invalid</c>.
        /// </exception>
        public async Task RenameAsync(TRoleId role, string name, string description, CancellationToken cancellationToken)
        {
            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireTenant();
            await gate.RequireTenantWideAsync(TenancyKeys.RolesManage, cancellationToken).ConfigureAwait(false);

            var renamed = await gate.LoadRoleAsync(role, cancellationToken).ConfigureAwait(false);
            await RequireNameFreeAsync(tenantId, name, except: role, cancellationToken).ConfigureAwait(false);
            renamed.Rename(name, description, gate.By);
            await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets the keys a role grants, for every seat that holds it. A seat that adds or takes out a key that
        /// manages access must be an administrator: hold <see cref="TenancyKeys.AdministratorKey"/> at the root
        /// with no end.
        /// </summary>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.not-permitted</c> without <see cref="TenancyKeys.RolesManage"/> for the whole tenant,
        /// <c>tenancy.role-not-found</c>, <c>tenancy.role-archived</c>, <c>tenancy.unknown-permission</c>,
        /// <c>tenancy.grant-exceeds-own</c> when a seat that is not an administrator adds or takes out a key that
        /// manages access, naming <see cref="TenancyKeys.AdministratorKey"/>, and <c>tenancy.last-admin</c> when
        /// the last administrators' role would lose the administrator key.
        /// </exception>
        public async Task SetKeysAsync(TRoleId role, IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
        {
            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireTenant();
            await gate.SerializeAsync(tenantId, cancellationToken).ConfigureAwait(false);
            await gate.RequireTenantWideAsync(TenancyKeys.RolesManage, cancellationToken).ConfigureAwait(false);

            var changed = await gate.LoadRoleAsync(role, cancellationToken).ConfigureAwait(false);
            var next = changed.KeysAfterSetting(keys, catalogue);
            if (next.Except(changed.Keys, StringComparer.Ordinal).Any(catalogue.ManagesAccess)
                || changed.Keys.Except(next, StringComparer.Ordinal).Any(catalogue.ManagesAccess))
            {
                await gate.RequireAdministratorAsync(tenantId, role, cancellationToken).ConfigureAwait(false);
            }

            if (!next.Contains(TenancyKeys.AdministratorKey, StringComparer.Ordinal))
            {
                await gate.EnsureAdministratorRemainsAsync(tenantId, AdministratorLoss.OfRole(role), cancellationToken).ConfigureAwait(false);
            }

            changed.SetKeys(keys, catalogue, gate.By);
            await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Archives a role: it stays where it was granted, and grants nothing from now on. A seat that archives a
        /// role that manages access must be an administrator: hold <see cref="TenancyKeys.AdministratorKey"/> at
        /// the root with no end.
        /// </summary>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.not-permitted</c> without <see cref="TenancyKeys.RolesManage"/> for the whole tenant,
        /// <c>tenancy.role-not-found</c>, <c>tenancy.role-archived</c>, <c>tenancy.grant-exceeds-own</c> when a
        /// seat that is not an administrator archives a role that manages access, naming
        /// <see cref="TenancyKeys.AdministratorKey"/>, and <c>tenancy.last-admin</c>.
        /// </exception>
        public async Task ArchiveAsync(TRoleId role, CancellationToken cancellationToken)
        {
            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireTenant();
            await gate.SerializeAsync(tenantId, cancellationToken).ConfigureAwait(false);
            await gate.RequireTenantWideAsync(TenancyKeys.RolesManage, cancellationToken).ConfigureAwait(false);

            var archived = await gate.LoadRoleAsync(role, cancellationToken).ConfigureAwait(false);
            if (catalogue.AccessManagingKeysOf(archived.Facts).Count > 0)
            {
                await gate.RequireAdministratorAsync(tenantId, role, cancellationToken).ConfigureAwait(false);
            }

            await gate.EnsureAdministratorRemainsAsync(tenantId, AdministratorLoss.OfRole(role), cancellationToken).ConfigureAwait(false);
            archived.Archive(gate.By);
            await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task RequireNameFreeAsync(TTenantId tenant, string name, TRoleId? except, CancellationToken cancellationToken)
        {
            var trimmed = name?.Trim() ?? string.Empty;
            if (trimmed.Length > 0 && await store.RoleNameTakenAsync(tenant, trimmed, except, cancellationToken).ConfigureAwait(false))
            {
                throw TenancyRefusals.Of(TenancyRefusals.RoleNameTaken, ("Name", trimmed));
            }
        }
    }
}
