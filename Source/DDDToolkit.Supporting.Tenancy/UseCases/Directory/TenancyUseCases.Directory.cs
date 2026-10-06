using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>
    /// What the screens of an application show about the tenant: who the caller is and what they may do
    /// where, the tenant's seats, roles and units for pickers, and the names of seats, roles and units by their
    /// ids. It reads, and changes nothing.
    /// <para>
    /// This is where a name comes from. The rows the access questions read
    /// (<see cref="ITenancyReadSource{TTenantId, TSeatId, TUnitId, TRoleId}"/>) carry no text that is shown to
    /// people, so a module that reads them next to its own tables answers ids, and whoever shows them asks here
    /// what they are called: <see cref="SeatsByIdAsync"/>, <see cref="RolesByIdAsync"/> and
    /// <see cref="UnitsByIdAsync(IReadOnlyCollection{TUnitId}, CancellationToken)"/>. The names are read from
    /// Tenancy's own seats, roles and units; a field the application added to its unit class is answered beside
    /// them through the overloads that take a view of the unit.
    /// </para>
    /// <para>
    /// Whoever works in a tenant reads its names: a seat of it, or system work in it. No key is asked, for a list
    /// or for a question by id, and a question by id answers any seat, role or unit of the caller's tenant; an id
    /// of another tenant, or of nothing, is left out of the answer without a word, so the answer never says which.
    /// A question by id takes at most <see cref="MostIds"/> ids.
    /// </para>
    /// <para>
    /// Paths are written from the root down, joined with <c>" / "</c>, and come from the closure of the tree,
    /// so a unit's path names the units above it even where the caller is not placed under those.
    /// </para>
    /// </summary>
    /// <param name="store">Where it reads.</param>
    /// <param name="catalogue">The catalogue, for the screens that assign roles.</param>
    /// <param name="clock">What "now" is, for whether a grant applies.</param>
    public sealed class TenancyDirectory(IStore store, TenancyCatalogue catalogue, TimeProvider clock)
    {
        /// <summary>How many ids one question by id takes.</summary>
        public const int MostIds = 200;

        /// <summary>Every permission key and role pack of the application.</summary>
        public TenancyCatalogue Catalogue => catalogue;

        /// <summary>
        /// Who the calling seat is: its tenant, where it is placed and with which roles, and every live key it
        /// holds now with where it is granted and every unit it reaches. A seat asks this about itself only.
        /// </summary>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.not-seated</c> for every caller that is not a seat: nobody, whatever it was refused for,
        /// and system work, which has no self to show.
        /// </exception>
        public async Task<SeatOverview> WhoAmIAsync(CancellationToken cancellationToken)
        {
            var gate = new Gate(store, catalogue, clock);
            if (gate.Caller.Kind != TenancyCallerKind.Seat)
            {
                throw TenancyRefusals.Of(TenancyRefusals.NotSeated);
            }

            var tenantId = gate.Caller.Tenant!.Value;
            var seatId = gate.Caller.Seat!.Value;
            var now = gate.Now;

            var tenant = await gate.LoadTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
            var organization = await gate.LoadOrganizationAsync(tenantId, cancellationToken).ConfigureAwait(false);
            var seat = await gate.LoadSeatAsync(seatId, cancellationToken).ConfigureAwait(false);
            var roles = (await store.ListRolesAsync(tenantId, cancellationToken).ConfigureAwait(false)).ToDictionary(role => role.Id);
            var units = await UnitMap.ReadAsync(store, organization, cancellationToken).ConfigureAwait(false);
            var rights = await store.Queries.ListAsync(
                store.Reads.SeatRights.Where(right => right.TenantId.Equals(tenantId) && right.SeatId.Equals(seatId)
                                                      && right.StartsAt <= now && (right.EndsAt == null || right.EndsAt > now)),
                cancellationToken).ConfigureAwait(false);

            var placements = seat.Placements
                .Select(placement => new PlacementSummary(
                    units.Ref(placement.UnitId),
                    placement.IsPrimary,
                    placement.Grants
                        .Select(grant => new GrantSummary(
                            grant.RoleId,
                            roles.TryGetValue(grant.RoleId, out var role) ? role.Name : string.Empty,
                            grant.StartsAt,
                            grant.EndsAt,
                            grant.AppliesAt(now)))
                        .OrderBy(grant => grant.Role, StringComparer.OrdinalIgnoreCase)
                        .ToArray()))
                .OrderByDescending(placement => placement.IsPrimary)
                .ThenBy(placement => placement.Unit.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var held = seat.Placements
                .SelectMany(placement => placement.Grants.Select(grant => grant.RoleId))
                .Distinct()
                .Select(role => roles.TryGetValue(role, out var found) ? Summary(found) : null)
                .OfType<RoleSummary>()
                .OrderBy(role => role.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // A key retired since the rights were written stays in them until the seat changes; it holds nowhere.
            var keys = rights
                .Where(right => catalogue.IsLive(right.Key))
                .GroupBy(right => right.Key, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group =>
                {
                    var grantedAt = group.Select(right => right.UnitId).Distinct().ToArray();
                    return new KeyReach(
                        group.Key,
                        units.Root is { } root && grantedAt.Contains(root),
                        units.Refs(grantedAt),
                        units.Refs(grantedAt.SelectMany(units.Subtree)));
                })
                .ToArray();

            return new SeatOverview(
                new TenantSummary(tenant.Id, tenant.Slug.Value, organization.Name, tenant.Shape, tenant.Status),
                new SeatSummary(seat.Id, seat.DisplayName, seat.Status),
                placements,
                held,
                keys);
        }

        /// <summary>The tenant's seats, by name: their names and statuses only.</summary>
        /// <exception cref="Exceptions.RefusalException">The caller is nobody.</exception>
        /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
        public async Task<IReadOnlyList<SeatSummary>> ListSeatsAsync(CancellationToken cancellationToken)
        {
            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireTenant();

            return ByName(await store.ListSeatsAsync(tenantId, only: null, cancellationToken).ConfigureAwait(false));
        }

        /// <summary>
        /// The seats among <paramref name="ids"/> that are in the caller's tenant, by name: their names and
        /// statuses only. An id of another tenant, or of no seat, is left out without a word: the answer never
        /// says which.
        /// </summary>
        /// <param name="ids">The seats asked about, at most <see cref="MostIds"/> different ones.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <exception cref="Exceptions.RefusalException">
        /// The caller is nobody, with its own code; or <c>tenancy.too-many-ids</c>.
        /// </exception>
        /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
        public async Task<IReadOnlyList<SeatSummary>> SeatsByIdAsync(IReadOnlyCollection<TSeatId> ids, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(ids);

            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireTenant();
            var asked = Asked(ids);
            if (asked.Count == 0)
            {
                return [];
            }

            return ByName(await store.ListSeatsAsync(tenantId, asked, cancellationToken).ConfigureAwait(false));
        }

        /// <summary>The tenant's roles, the active ones first, each by name.</summary>
        /// <exception cref="Exceptions.RefusalException">The caller is nobody.</exception>
        /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
        public async Task<IReadOnlyList<RoleSummary>> ListRolesAsync(CancellationToken cancellationToken)
        {
            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireTenant();

            return ActiveFirst(await store.ListRolesAsync(tenantId, cancellationToken).ConfigureAwait(false));
        }

        /// <summary>
        /// The roles among <paramref name="ids"/> that are the caller's tenant's, the active ones first, each by
        /// name, with whether it manages access. An id of another tenant, or of no role, is left out without a
        /// word.
        /// </summary>
        /// <param name="ids">The roles asked about, at most <see cref="MostIds"/> different ones.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <exception cref="Exceptions.RefusalException">
        /// The caller is nobody, with its own code; or <c>tenancy.too-many-ids</c>.
        /// </exception>
        /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
        public async Task<IReadOnlyList<RoleSummary>> RolesByIdAsync(IReadOnlyCollection<TRoleId> ids, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(ids);

            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireTenant();
            var asked = Asked(ids);
            if (asked.Count == 0)
            {
                return [];
            }

            // A tenant has few roles, so they are read in one statement and the ones asked for picked from them.
            var roles = await store.ListRolesAsync(tenantId, cancellationToken).ConfigureAwait(false);
            return ActiveFirst(roles.Where(role => asked.Contains(role.Id)));
        }

        /// <summary>
        /// The units the caller reads, by path: for a seat, the units it is placed in and every unit below them;
        /// for system work in the tenant, every unit.
        /// </summary>
        /// <exception cref="Exceptions.RefusalException">The caller is nobody.</exception>
        /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
        public Task<IReadOnlyList<UnitSummary>> ListUnitsAsync(CancellationToken cancellationToken)
            => ListUnitsAsync(static (summary, _) => summary, cancellationToken);

        /// <summary>
        /// The units the caller reads, as <see cref="ListUnitsAsync(CancellationToken)"/> answers them, each
        /// answered as <paramref name="view"/> makes it of the package's summary and the application's own unit.
        /// That is how a field the application added to its unit class, such as what kind of unit it is, is
        /// answered beside what Tenancy keeps: from the units the directory read anyway, with no statement more.
        /// </summary>
        /// <typeparam name="TView">What the application answers of a unit.</typeparam>
        /// <param name="view">
        /// Makes the answer of one unit, once for each, by path. The unit is the organization's own, read for this
        /// answer: read it, and change nothing on it.
        /// </param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <exception cref="Exceptions.RefusalException">The caller is nobody.</exception>
        /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
        public async Task<IReadOnlyList<TView>> ListUnitsAsync<TView>(Func<UnitSummary, TUnit, TView> view, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(view);

            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireTenant();

            var organization = await gate.LoadOrganizationAsync(tenantId, cancellationToken).ConfigureAwait(false);
            var units = await UnitMap.ReadAsync(store, organization, cancellationToken).ConfigureAwait(false);
            IEnumerable<TUnitId> readable = gate.BySystem
                ? units.Ids
                : await store.Queries.ListAsync(gate.Questions.ReadableUnits(), cancellationToken).ConfigureAwait(false);

            return units.Summaries(readable, view);
        }

        /// <summary>
        /// The units among <paramref name="ids"/> that are the caller's tenant's, by path, whichever of them the
        /// caller is placed under: a seat that works on something at a unit it is not placed under still reads
        /// what that unit is called. An id of another tenant, or of no unit, is left out without a word.
        /// </summary>
        /// <param name="ids">The units asked about, at most <see cref="MostIds"/> different ones.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <exception cref="Exceptions.RefusalException">
        /// The caller is nobody, with its own code; or <c>tenancy.too-many-ids</c>.
        /// </exception>
        /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
        public Task<IReadOnlyList<UnitSummary>> UnitsByIdAsync(IReadOnlyCollection<TUnitId> ids, CancellationToken cancellationToken)
            => UnitsByIdAsync(ids, static (summary, _) => summary, cancellationToken);

        /// <summary>
        /// The units among <paramref name="ids"/>, as <see cref="UnitsByIdAsync(IReadOnlyCollection{TUnitId}, CancellationToken)"/>
        /// answers them, each answered as <paramref name="view"/> makes it of the package's summary and the
        /// application's own unit: a field the application added to its unit class, beside what Tenancy keeps,
        /// with no statement more.
        /// </summary>
        /// <typeparam name="TView">What the application answers of a unit.</typeparam>
        /// <param name="ids">The units asked about, at most <see cref="MostIds"/> different ones.</param>
        /// <param name="view">
        /// Makes the answer of one unit, once for each, by path. The unit is the organization's own, read for this
        /// answer: read it, and change nothing on it.
        /// </param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <exception cref="Exceptions.RefusalException">
        /// The caller is nobody, with its own code; or <c>tenancy.too-many-ids</c>.
        /// </exception>
        /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
        public async Task<IReadOnlyList<TView>> UnitsByIdAsync<TView>(
            IReadOnlyCollection<TUnitId> ids,
            Func<UnitSummary, TUnit, TView> view,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(ids);
            ArgumentNullException.ThrowIfNull(view);

            var gate = new Gate(store, catalogue, clock);
            var tenantId = gate.RequireTenant();
            var asked = Asked(ids);
            if (asked.Count == 0)
            {
                return [];
            }

            var organization = await gate.LoadOrganizationAsync(tenantId, cancellationToken).ConfigureAwait(false);
            var units = await UnitMap.ReadAsync(store, organization, cancellationToken).ConfigureAwait(false);
            return units.Summaries(asked, view);
        }

        /// <summary>
        /// The different ids among <paramref name="ids"/>. Refused when they are more than one question takes;
        /// asked after the caller, so nobody learns the limit before its own refusal.
        /// </summary>
        /// <exception cref="Exceptions.RefusalException"><c>tenancy.too-many-ids</c>, with <c>Max</c>.</exception>
        private static HashSet<TId> Asked<TId>(IReadOnlyCollection<TId> ids)
            where TId : struct, IEquatable<TId>
        {
            var asked = new HashSet<TId>(ids);
            return asked.Count > MostIds
                ? throw TenancyRefusals.Of(TenancyRefusals.TooManyIds, ("Max", MostIds))
                : asked;
        }

        /// <summary>By name, and by id where two seats are shown by the same name, so the order is the same every time.</summary>
        private static SeatSummary[] ByName(IEnumerable<SeatSummary> seats)
            => [.. seats
                .OrderBy(seat => seat.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(seat => seat.Id.ToString(), StringComparer.Ordinal)];

        private RoleSummary[] ActiveFirst(IEnumerable<TRole> roles)
            => [.. roles
                .Select(Summary)
                .OrderBy(role => role.Status != RoleStatus.Active)
                .ThenBy(role => role.Name, StringComparer.OrdinalIgnoreCase)];

        private RoleSummary Summary(TRole role)
            => new(role.Id, role.Name, role.FromPack, role.Status, role.Keys, catalogue.AccessManagingKeysOf(role.Facts).Count > 0);
    }

    /// <summary>
    /// A tenant's units and the closure of its tree, read once, to name units by their path from the root and
    /// to find every unit below a unit. The units are the organization's own, which carry the names; the rows the
    /// access questions read have none.
    /// </summary>
    private sealed class UnitMap
    {
        private readonly Dictionary<TUnitId, TUnit> _units;
        private readonly Dictionary<TUnitId, List<OrganizationUnitPath<TTenantId, TUnitId>>> _above = [];
        private readonly Dictionary<TUnitId, List<TUnitId>> _below = [];

        private UnitMap(IReadOnlyList<TUnit> units, IReadOnlyList<OrganizationUnitPath<TTenantId, TUnitId>> paths)
        {
            _units = units.ToDictionary(unit => unit.Id);
            Root = units.FirstOrDefault(unit => unit.IsRoot)?.Id;

            foreach (var path in paths)
            {
                if (!_above.TryGetValue(path.DescendantId, out var above))
                {
                    _above[path.DescendantId] = above = [];
                }

                above.Add(path);

                if (!_below.TryGetValue(path.AncestorId, out var below))
                {
                    _below[path.AncestorId] = below = [];
                }

                below.Add(path.DescendantId);
            }
        }

        /// <summary>The root, or <see langword="null"/> when the organization has no units.</summary>
        public TUnitId? Root { get; }

        /// <summary>Every unit's id.</summary>
        public IEnumerable<TUnitId> Ids => _units.Keys;

        /// <summary>The map of <paramref name="organization"/>, which is loaded already: one read, of the closure.</summary>
        public static async Task<UnitMap> ReadAsync(IStore store, TOrganization organization, CancellationToken cancellationToken)
        {
            var tenant = organization.Id;
            var paths = await store.Queries.ListAsync(store.Reads.UnitPaths.Where(path => path.TenantId.Equals(tenant)), cancellationToken).ConfigureAwait(false);
            return new UnitMap(organization.Units, paths);
        }

        /// <summary>The unit, or <see langword="null"/> when the organization has none with the id.</summary>
        public TUnit? Unit(TUnitId id) => _units.GetValueOrDefault(id);

        /// <summary>The unit and every unit below it.</summary>
        public IEnumerable<TUnitId> Subtree(TUnitId id) => _below.TryGetValue(id, out var below) ? below : [id];

        /// <summary>The names of the unit and the units above it, the root first.</summary>
        public string PathOf(TUnitId id)
            => _above.TryGetValue(id, out var above)
                ? string.Join(" / ", above.OrderByDescending(path => path.Distance).Select(path => _units.TryGetValue(path.AncestorId, out var unit) ? unit.Name : path.AncestorId.ToString()))
                : _units.TryGetValue(id, out var own) ? own.Name : id.ToString() ?? string.Empty;

        /// <summary>How deep the unit is, the root being 1.</summary>
        public int DepthOf(TUnitId id) => _above.TryGetValue(id, out var above) ? above.Count : 1;

        public UnitRef Ref(TUnitId id) => new(id, PathOf(id));

        /// <summary>The units, each once, by path.</summary>
        public IReadOnlyList<UnitRef> Refs(IEnumerable<TUnitId> ids)
            => ids.Distinct().Select(Ref).OrderBy(unit => unit.Path, StringComparer.OrdinalIgnoreCase).ToArray();

        /// <summary>
        /// The units among <paramref name="ids"/> that the organization has, each once, by path, each answered as
        /// <paramref name="view"/> makes it of its summary and the unit itself.
        /// </summary>
        public IReadOnlyList<TView> Summaries<TView>(IEnumerable<TUnitId> ids, Func<UnitSummary, TUnit, TView> view)
            => ids
                .Distinct()
                .Select(Unit)
                .OfType<TUnit>()
                .Select(unit => (Summary: new UnitSummary(unit.Id, unit.ParentId, unit.Name, unit.Status, PathOf(unit.Id), DepthOf(unit.Id)), Unit: unit))
                .OrderBy(pair => pair.Summary.Path, StringComparer.OrdinalIgnoreCase)
                .Select(pair => view(pair.Summary, pair.Unit))
                .ToArray();
    }
}
