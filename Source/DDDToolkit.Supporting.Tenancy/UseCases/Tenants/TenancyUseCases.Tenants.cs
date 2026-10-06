using System.Globalization;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>
    /// A tenant's life and settings: provisioning it with everything it needs to be used, changing its shape
    /// and name, and suspending, reactivating or closing it.
    /// <para>
    /// Provisioning is the one thing system work outside any tenant does. Suspending, reactivating and closing
    /// are system work inside the tenant, and act on that tenant: an operator begins
    /// <c>TenancyWork.BeginSystemIn(tenant)</c> first, so acting on another tenant cannot be expressed.
    /// </para>
    /// </summary>
    /// <param name="store">Where the tenant is loaded and saved.</param>
    /// <param name="catalogue">The packs a new tenant is given.</param>
    /// <param name="options">How new ids are made.</param>
    /// <param name="clock">What "now" is.</param>
    /// <param name="packTexts">
    /// The packs' names and descriptions in a tenant's language, when the application registered some;
    /// otherwise every role made from a pack gets the catalogue's texts, but the role of the default
    /// administrators' pack, which the package adds, gets the package's own texts in that language, English or
    /// Dutch.
    /// </param>
    public sealed class TenantCommands(
        IStore store,
        TenancyCatalogue catalogue,
        TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId> options,
        TimeProvider clock,
        IRolePackTexts? packTexts = null)
    {
        /// <summary>The reason recorded on the grant that makes the first seat an administrator.</summary>
        private const string ProvisionedReason = "first administrator";

        /// <summary>
        /// Provisions a tenant, in one save: the tenant, its organization with the root, a role copied from
        /// every pack of its shape, and a first seat for <see cref="TenantToProvision.AdminIdentity"/>, placed
        /// at the root and granted the administrators' role there with no end. That role holds what the
        /// administrators' pack of the shape holds, which is every live key unless the pack lists its own. The
        /// tenant is active when the save is done.
        /// <para>
        /// The roles are named as the catalogue names its packs, or, with a
        /// <see cref="TenantToProvision.Language"/> and an <see cref="IRolePackTexts"/> registered, in that
        /// language. The role of <see cref="TenancyPacks.DefaultAdministrators"/>, for an application that declares
        /// no administrators' pack, is named in that language by the application's texts, or else by the package's
        /// own, in English or Dutch. The fields the application added to its tenant, unit and seat classes are set
        /// by <see cref="TenantToProvision.ConfigureTenant"/>, <see cref="TenantToProvision.ConfigureRoot"/> and
        /// <see cref="TenantToProvision.ConfigureFirstSeat"/>, before the tenant is activated and in the same save.
        /// </para>
        /// <para>
        /// Only system work outside any tenant provisions one, and it does not do so with that power: once it
        /// knows the new tenant's id, everything else, the check that the slug is free and the save included,
        /// runs as system work inside the new tenant (<see cref="TenancyWork.BeginSystemIn{TTenantId, TSeatId}"/>),
        /// so it writes that tenant's rows and nothing else. On a database that keeps tenants apart by itself,
        /// such as Postgres with row level security, the slug check then sees no other tenant, and the unique
        /// index on the slug is what refuses one that is taken.
        /// </para>
        /// </summary>
        /// <param name="command">What to provision.</param>
        /// <param name="cancellationToken">Cancels the work.</param>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>access.system-only</c> for a seat, <c>tenancy.slug-taken</c>, <c>tenancy.role-name-taken</c> when two
        /// packs are named alike in the tenant's language, and what the aggregates refuse: an invalid name, a pack's
        /// translated one included, or an empty identity.
        /// </exception>
        /// <exception cref="Exceptions.InvalidValueObjectException">The slug does not follow <see cref="TenantSlug.Pattern"/>.</exception>
        /// <exception cref="ArgumentException"><see cref="TenantToProvision.RoleIds"/> names a pack the tenant is not given, or gives one id twice.</exception>
        /// <exception cref="InvalidOperationException">The caller is system work in a tenant rather than outside any.</exception>
        public async Task<ProvisionedTenant> ProvisionAsync(TenantToProvision command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);

            var gate = new Gate(store, catalogue, clock);
            gate.RequireSystemOutsideTenants();
            var ids = options.Checked();

            var packs = catalogue.PacksFor(command.Shape).ToArray();
            RequireSeededPacks(command.RoleIds, packs, command.Shape);

            var slug = TenantSlug.Create(command.Slug).ToValid();
            var tenantId = command.TenantId ?? ids.NewTenantId!();

            // From here on the work acts in the tenant it makes and in no other: every read and the save. It is
            // still recorded as whoever began it: the system, or the operator the tenant is provisioned for, on
            // the save and on every event of the new tenant.
            var by = gate.By ?? TenancyActor<TSeatId>.OfSystem(TenancyWork.SystemScope);
            using (TenancyWork.BeginSystemInAs(tenantId, by))
            {
                return await ProvisionInAsync(command, tenantId, slug, packs, gate.Now, by, ids, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>Everything of <see cref="ProvisionAsync"/> after the new tenant's id is known, run as system work in that tenant.</summary>
        private async Task<ProvisionedTenant> ProvisionInAsync(
            TenantToProvision command,
            TTenantId tenantId,
            ValidTenantSlug slug,
            RolePack[] packs,
            DateTimeOffset now,
            TenancyActor<TSeatId> by,
            TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId> ids,
            CancellationToken cancellationToken)
        {
            if (await store.SlugTakenAsync(slug.Value, cancellationToken).ConfigureAwait(false))
            {
                throw TenancyRefusals.Of(TenancyRefusals.SlugTaken, ("Slug", slug.Value));
            }

            var rootId = command.RootId ?? ids.NewUnitId!();
            var tenant = TenancyInstances.NewTenant<TTenant, TTenantId, TSeatId>(tenantId, slug, command.Shape, by);
            var organization = TenancyInstances.NewOrganization<TOrganization, TTenantId, TUnit, TUnitId, TSeatId>(
                tenantId, command.Name, rootId, command.RootName, by);

            // The catalogue keeps its packs' own names apart. Names in another language are the application's,
            // so they are held apart here, the way a role made by hand is held apart from the others.
            var roles = new Dictionary<string, TRole>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pack in packs)
            {
                var role = NewRoleFrom(DraftOf(pack, command.Language), tenantId, command.RoleIds, ids, catalogue, by);
                if (!names.Add(role.Name))
                {
                    throw TenancyRefusals.Of(TenancyRefusals.RoleNameTaken, ("Name", role.Name));
                }

                roles[pack.Key] = role;
            }

            var administrators = roles[catalogue.AdministratorPackFor(command.Shape).Key];
            var seatId = command.AdminSeatId ?? ids.NewSeatId!();
            var seat = TenancyInstances.NewSeat<TSeat, TSeatId, TTenantId, TUnitId, TRoleId>(seatId, tenantId, command.AdminIdentity, command.AdminDisplayName, by);
            seat.Place(rootId, primary: true, now, placedBy: null, by);
            seat.Grant(rootId, administrators.Id, administrators.Facts, GrantPeriod.Open(now), grantedBy: null, ProvisionedReason, by);

            // The application's own fields, before anything is handed to the store: a callback that throws
            // leaves nothing to save, and what it sets is written with everything else.
            command.ConfigureTenant?.Invoke(tenant);
            command.ConfigureRoot?.Invoke(organization.Root);
            command.ConfigureFirstSeat?.Invoke(seat);
            tenant.Activate<TSeatId>(by);

            store.Add(tenant);
            store.Add(organization);
            foreach (var role in roles.Values)
            {
                store.Add(role);
            }

            store.Add(seat);
            store.AddAccessRevision(tenantId);
            await store.SaveAsync(cancellationToken).ConfigureAwait(false);

            return new ProvisionedTenant(
                tenantId,
                rootId,
                seatId,
                administrators.Id,
                roles.ToDictionary(pair => pair.Key, pair => pair.Value.Id, StringComparer.Ordinal));
        }

        /// <summary>
        /// Changes a flat tenant into a hierarchical one, and gives it a copy of every pack of the new shape it
        /// has no copy of yet. No grant changes: who held what at the root still does. Where the two shapes have
        /// administrators' packs of their own, the tenant gets the role of the new shape's pack with the keys that
        /// pack holds, and nobody is given it: the administrators keep the role they have, and give the new one
        /// as they see fit.
        /// </summary>
        /// <param name="to">The new shape.</param>
        /// <param name="roleIds">The ids of the new roles, by pack key, for imports and seeding; the rest get new ids.</param>
        /// <param name="language">
        /// The language the new roles are named in, usually the tenant's, which the application keeps: each
        /// pack's texts are asked of the application's <see cref="IRolePackTexts"/> in it. <see langword="null"/>,
        /// or an application that registered no texts, keeps the catalogue's own; the default administrators'
        /// pack, which the package adds, gets the package's own texts in that language, English or Dutch, where
        /// the application's have none for it. The roles the tenant has already keep their names.
        /// </param>
        /// <param name="cancellationToken">Cancels the work.</param>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.not-permitted</c> without <see cref="TenancyKeys.SettingsManage"/> for the whole tenant,
        /// <c>tenancy.shape-change</c>, <c>tenancy.tenant-state</c> for a closed tenant,
        /// <c>tenancy.role-name-taken</c> when a role of the tenant, or another new role, already has a new
        /// role's name, <c>tenancy.name-invalid</c> for a pack's translated name or description.
        /// </exception>
        /// <exception cref="ArgumentException"><paramref name="roleIds"/> names a pack of no role that would be made, or gives one id twice.</exception>
        public async Task ChangeShapeAsync(
            TenantShape to,
            IReadOnlyDictionary<string, TRoleId>? roleIds,
            CultureInfo? language,
            CancellationToken cancellationToken)
        {
            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireTenant();
            await gate.RequireTenantWideAsync(TenancyKeys.SettingsManage, cancellationToken).ConfigureAwait(false);
            var ids = options.Checked();

            // Everything is decided before the tenant changes, so a refusal leaves nothing for a later save.
            var tenant = await gate.LoadTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
            tenant.RequireShapeChange(to);

            var copied = (await store.ListRolesAsync(tenantId, cancellationToken).ConfigureAwait(false))
                .Select(role => role.FromPack)
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);
            var missing = catalogue.PacksFor(to).Where(pack => !copied.Contains(pack.Key)).ToArray();
            RequireSeededPacks(roleIds, missing, to);

            var added = new List<TRole>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pack in missing)
            {
                var draft = DraftOf(pack, language);
                var name = draft.Name?.Trim() ?? string.Empty;
                // A blank name is the role's to refuse, as invalid, when it is made below.
                if (name.Length > 0
                    && (!names.Add(name) || await store.RoleNameTakenAsync(tenantId, name, null, cancellationToken).ConfigureAwait(false)))
                {
                    throw TenancyRefusals.Of(TenancyRefusals.RoleNameTaken, ("Name", name));
                }

                added.Add(NewRoleFrom(draft, tenantId, roleIds, ids, catalogue, gate.By));
            }

            tenant.ChangeShape(to, gate.By);
            foreach (var role in added)
            {
                store.Add(role);
            }

            await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Renames the organization, which is the tenant's name.</summary>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.not-permitted</c> without <see cref="TenancyKeys.SettingsManage"/> for the whole tenant,
        /// <c>tenancy.name-invalid</c>.
        /// </exception>
        public async Task RenameOrganizationAsync(string name, CancellationToken cancellationToken)
        {
            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireTenant();
            await gate.RequireTenantWideAsync(TenancyKeys.SettingsManage, cancellationToken).ConfigureAwait(false);

            var organization = await gate.LoadOrganizationAsync(tenantId, cancellationToken).ConfigureAwait(false);
            organization.Rename(name, gate.By);
            await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Suspends the caller's tenant: system work in that tenant only.</summary>
        /// <param name="reason">Why; required.</param>
        /// <param name="cancellationToken">Cancels the work.</param>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>access.system-only</c> for a seat, <c>tenancy.tenant-state</c>, <c>tenancy.reason-required</c>.
        /// </exception>
        /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
        public Task SuspendAsync(string reason, CancellationToken cancellationToken)
            => ChangeStatusAsync((tenant, by) => tenant.Suspend(reason, by), cancellationToken);

        /// <summary>Puts the caller's suspended tenant back in use: system work in that tenant only.</summary>
        /// <exception cref="Exceptions.RefusalException"><c>access.system-only</c> for a seat, <c>tenancy.tenant-state</c>.</exception>
        /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
        public Task ReactivateAsync(CancellationToken cancellationToken)
            => ChangeStatusAsync((tenant, by) => tenant.Reactivate(by), cancellationToken);

        /// <summary>Closes the caller's tenant for good: system work in that tenant only.</summary>
        /// <param name="reason">Why; required.</param>
        /// <param name="cancellationToken">Cancels the work.</param>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>access.system-only</c> for a seat, <c>tenancy.tenant-state</c>, <c>tenancy.reason-required</c>.
        /// </exception>
        /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
        public Task CloseAsync(string reason, CancellationToken cancellationToken)
            => ChangeStatusAsync((tenant, by) => tenant.Close(reason, by), cancellationToken);

        private async Task ChangeStatusAsync(Action<TTenant, TenancyActor<TSeatId>?> change, CancellationToken cancellationToken)
        {
            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireSystemInTenant("A tenant is suspended, reactivated or closed");

            var tenant = await gate.LoadTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
            change(tenant, gate.By);
            await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// What a role copied from <paramref name="pack"/> starts as. It holds the keys the catalogue built the
        /// pack with, the administrators' role too: every live key for a pack that lists none, and for one that
        /// lists keys those and no others. Its name and description are the application's texts for the pack in
        /// <paramref name="language"/> when it has some, and the catalogue's otherwise. The default
        /// administrators' pack, which the application did not declare, has the package's texts in between: a
        /// tenant in Dutch gets it in Dutch, as it gets the package's refusals.
        /// </summary>
        private RoleDraft DraftOf(RolePack pack, CultureInfo? language)
            => language is not null && (packTexts?.For(pack, language) ?? PackageTextsOf(pack, language)) is { } texts
                ? new RoleDraft(texts.Name, texts.Description, pack.Keys, pack.Key)
                : new RoleDraft(pack.Name, pack.Description, pack.Keys, pack.Key);

        /// <summary>The package's texts for its own pack, the default administrators' one, in <paramref name="language"/>; none for the application's packs.</summary>
        private (string Name, string Description)? PackageTextsOf(RolePack pack, CultureInfo language)
            => catalogue.HasDefaultAdministrators && string.Equals(pack.Key, TenancyPacks.DefaultAdministratorsKey, StringComparison.Ordinal)
                ? TenancyPackTexts.DefaultAdministratorsIn(language)
                : null;

        /// <summary>
        /// A role made from the <paramref name="draft"/> of a pack, with the id given for the pack or a new one.
        /// The role checks the draft's name and description as it checks any role's.
        /// </summary>
        private static TRole NewRoleFrom(
            RoleDraft draft,
            TTenantId tenant,
            IReadOnlyDictionary<string, TRoleId>? roleIds,
            TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId> ids,
            TenancyCatalogue catalogue,
            TenancyActor<TSeatId>? by)
        {
            var id = roleIds is not null && roleIds.TryGetValue(draft.FromPack!, out var given) ? given : ids.NewRoleId!();
            return TenancyInstances.NewRole<TRole, TRoleId, TTenantId, TSeatId>(id, tenant, draft, catalogue, by);
        }

        /// <summary>
        /// Ids given by pack key name only packs a role is about to be made from, since a typo would otherwise be
        /// ignored, and give each id once, since two roles would otherwise share it.
        /// </summary>
        private static void RequireSeededPacks(IReadOnlyDictionary<string, TRoleId>? roleIds, IReadOnlyCollection<RolePack> packs, TenantShape shape)
        {
            if (roleIds is null)
            {
                return;
            }

            var unknown = roleIds.Keys
                .Where(key => !packs.Any(pack => string.Equals(pack.Key, key, StringComparison.Ordinal)))
                .ToArray();
            if (unknown.Length > 0)
            {
                throw new ArgumentException(
                    "Role ids are given for " + string.Join(", ", unknown) + ", but no role of a " + shape.ToString().ToLowerInvariant()
                    + " tenant is made from such a pack here. Packs: " + string.Join(", ", packs.Select(pack => pack.Key)) + ".",
                    nameof(roleIds));
            }

            var shared = roleIds.GroupBy(pair => pair.Value).Where(group => group.Count() > 1).ToArray();
            if (shared.Length > 0)
            {
                throw new ArgumentException(
                    "Role ids are given more than once: " + string.Join("; ", shared.Select(group => group.Key + " for "
                        + string.Join(", ", group.Select(pair => pair.Key).Order(StringComparer.Ordinal)))) + ". Each role needs an id of its own.",
                    nameof(roleIds));
            }
        }
    }
}
