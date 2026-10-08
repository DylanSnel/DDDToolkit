using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>
    /// The tenant's roles: making them, renaming them, changing their keys and archiving them. Each needs
    /// <see cref="TenancyKeys.RolesManage"/> for the whole tenant. Following the packs the roles were made from,
    /// once the application changed them, is system work's alone (<see cref="FollowPacksAsync"/>).
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
    /// <para>
    /// That exception is containment, and goes when the application turns containment off
    /// (<see cref="ApplicationCatalogue.ContainAccessManagingKeys"/>): then the role key for the whole tenant is all
    /// any change of a role takes. The administrator a tenant keeps stays either way.
    /// </para>
    /// </summary>
    /// <param name="store">Where roles are loaded and saved.</param>
    /// <param name="catalogue">The keys a role may hold.</param>
    /// <param name="clock">What "now" is.</param>
    public sealed class RoleCommands(
        IStore store,
        TenancyCatalogue catalogue,
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
                id ?? TRoleId.Create(), tenantId, new RoleDraft(name, description, keys), catalogue, gate.By);
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
        /// Sets the keys a role grants, for every seat that holds it. While containment is on
        /// (<see cref="ApplicationCatalogue.ContainAccessManagingKeys"/>), a seat that adds or takes out a key that
        /// manages access must be an administrator: hold <see cref="TenancyKeys.AdministratorKey"/> at the root
        /// with no end.
        /// </summary>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.not-permitted</c> without <see cref="TenancyKeys.RolesManage"/> for the whole tenant,
        /// <c>tenancy.role-not-found</c>, <c>tenancy.role-archived</c>, <c>tenancy.unknown-permission</c>,
        /// <c>tenancy.grant-exceeds-own</c> when a seat that is not an administrator adds or takes out a key that
        /// manages access while containment is on, naming <see cref="TenancyKeys.AdministratorKey"/>, and
        /// <c>tenancy.last-admin</c> when
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
        /// Archives a role: it stays where it was granted, and grants nothing from now on. While containment is on
        /// (<see cref="ApplicationCatalogue.ContainAccessManagingKeys"/>), a seat that archives a role that manages
        /// access must be an administrator: hold <see cref="TenancyKeys.AdministratorKey"/> at the root with no end.
        /// </summary>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.not-permitted</c> without <see cref="TenancyKeys.RolesManage"/> for the whole tenant,
        /// <c>tenancy.role-not-found</c>, <c>tenancy.role-archived</c>, <c>tenancy.grant-exceeds-own</c> when a
        /// seat that is not an administrator archives a role that manages access while containment is on, naming
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

        /// <summary>
        /// Every active role of the caller's tenant that was made from a pack follows that pack, as the catalogue the
        /// application runs with builds it now (<see cref="RoleAggregate{TRoleId, TTenantId}.FollowPack{TSeatId}"/>):
        /// a key the pack gained since is added, a key it lost is taken out, and what the tenant changed itself stays.
        /// Each role whose keys change raises <see cref="RoleFollowedItsPack{TTenantId, TRoleId, TSeatId}"/>, and the
        /// change reaches every seat that holds the role at the save, the one save of the command.
        /// <para>
        /// System work in the tenant alone runs it, as <c>services.SyncRolePacks()</c> does for every tenant once
        /// the host has started, through <see cref="IRolePackSync"/>; an operator's request begins
        /// <c>TenancyWork.BeginOperatorIn(tenant, operator)</c> for one tenant, and the events name the operator. No
        /// seat runs it: what a pack holds is the application's decision, keys that manage access included, and an
        /// administrator changes a role with <see cref="SetKeysAsync"/>.
        /// </para>
        /// <para>
        /// It takes the tenant's access revision first, as every change of rights does, and saves only when a role
        /// changed: a second run in a row reads, and writes nothing. Two runs at the same time cannot both commit; the
        /// one that fails on the revision changes nothing when it runs again. The last-administrator rule holds for
        /// it as for anyone: a role whose following would take the administrator key from the tenant's last
        /// administrators is left as it is, and named in the answer.
        /// </para>
        /// </summary>
        /// <param name="cancellationToken">Cancels the work.</param>
        /// <returns>The roles that changed, the roles kept for an administrator, and the roles whose pack is gone.</returns>
        /// <exception cref="Exceptions.RefusalException"><c>access.system-only</c> for a seat.</exception>
        /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
        public async Task<PacksFollowed> FollowPacksAsync(CancellationToken cancellationToken)
        {
            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireSystemInTenant("A tenant's roles are made to follow their packs");
            await gate.SerializeAsync(tenantId, cancellationToken).ConfigureAwait(false);

            var roles = (await store.ListRolesAsync(tenantId, cancellationToken).ConfigureAwait(false))
                .Where(role => role.FromPack is not null && role.Status == RoleStatus.Active)
                .OrderBy(role => role.FromPack, StringComparer.Ordinal)
                .ToArray();

            // Everything is decided before a role changes, so a refusal leaves nothing for a later save to write. The
            // administrators as last saved are read once, and only when a role would lose the administrator key: the
            // roles that lose it are counted together, so two of them cannot each leave the other's administrators.
            var following = new List<TRole>();
            var kept = new List<TRoleId>();
            var gone = new List<TRoleId>();
            var losing = new HashSet<TRoleId>();
            IReadOnlyList<(TSeatId Seat, TRoleId Role)>? administrators = null;
            foreach (var role in roles)
            {
                if (role.KeysAfterFollowing(catalogue) is not { } next)
                {
                    gone.Add(role.Id);
                    continue;
                }

                if (role.Holds(TenancyKeys.AdministratorKey) && !next.Keys.Contains(TenancyKeys.AdministratorKey, StringComparer.Ordinal))
                {
                    administrators ??= await store.AdministratorsAsync(tenantId, gate.Now, cancellationToken).ConfigureAwait(false);
                    if (administrators.Count > 0 && administrators.All(pair => losing.Contains(pair.Role) || pair.Role.Equals(role.Id)))
                    {
                        kept.Add(role.Id);
                        continue;
                    }

                    losing.Add(role.Id);
                }

                following.Add(role);
            }

            // A role whose keys changed raised its event. One that saves only what it remembers of its pack, or its keys
            // in the order a role keeps them, is saved without one: the same keys are no change to tell anyone about.
            var changed = new List<TRoleId>();
            var saves = false;
            foreach (var role in following)
            {
                var before = role.Keys.ToHashSet(StringComparer.Ordinal);
                if (role.FollowPack(catalogue, gate.By))
                {
                    saves = true;
                    if (!before.SetEquals(role.Keys))
                    {
                        changed.Add(role.Id);
                    }
                }
            }

            if (saves)
            {
                await store.SaveAsync(cancellationToken).ConfigureAwait(false);
            }

            return new PacksFollowed(changed, kept, gone);
        }

        private async Task RequireNameFreeAsync(TTenantId tenant, string name, TRoleId? except, CancellationToken cancellationToken)
        {
            var trimmed = name?.Trim() ?? string.Empty;
            if (trimmed.Length > 0 && await store.RoleNameTakenAsync(tenant, trimmed, except, cancellationToken).ConfigureAwait(false))
            {
                throw TenancyRefusals.Refuse(TenancyRefusals.RoleNameTaken, ("Name", trimmed));
            }
        }
    }
}
