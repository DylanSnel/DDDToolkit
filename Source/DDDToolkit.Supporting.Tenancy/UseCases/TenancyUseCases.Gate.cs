using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>
    /// What every command asks before it acts: who is calling, whether they hold the key where the command
    /// needs it, whether a seat may give or take away a role there (freely for a role that manages no access,
    /// only with its keys that do for one that does, and never one that does to itself), whether it may stop
    /// or restart a seat that holds such a role, whether a move would give or take away what it could not, and
    /// whether the tenant keeps an administrator. One per command call: it fixes the caller and "now" when the
    /// command starts.
    /// <para>
    /// The rules in the middle hold a seat to containment while the catalogue keeps the keys that manage access
    /// contained (<see cref="TenancyCatalogue.ContainAccessManagingKeys"/>). Once the application turns that off,
    /// they ask of a role or a key that manages access what they ask of one that manages none
    /// (<see cref="ContainedKeysOf"/> and <see cref="IsContained"/>). The key a command asks where it acts, a
    /// seat's grant to itself against its own <see cref="TenancyKeys.GrantsManage"/>, what a move gives the mover,
    /// and the administrator a tenant keeps, are asked either way.
    /// </para>
    /// <para>
    /// Asking who is calling reads nothing, so a command that takes the access revision can take it before any
    /// other read; the questions are only made, over the store's reads, when a key is first asked about.
    /// </para>
    /// </summary>
    internal sealed class Gate
    {
        private readonly IStore _store;
        private readonly TenancyCatalogue _catalogue;
        private ITenancyQuestions<TTenantId, TSeatId, TUnitId, TRoleId>? _questions;

        public Gate(IStore store, TenancyCatalogue catalogue, TimeProvider clock)
        {
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(catalogue);
            ArgumentNullException.ThrowIfNull(clock);

            _store = store;
            _catalogue = catalogue;
            Caller = TenancyCallers.Current<TTenantId, TSeatId>();
            Now = clock.GetUtcNow();
        }

        /// <summary>The caller the command runs for.</summary>
        public TenancyCaller<TTenantId, TSeatId> Caller { get; }

        /// <summary>The moment the command runs at.</summary>
        public DateTimeOffset Now { get; }

        /// <summary>Whether this is system work in a tenant, which holds every key there.</summary>
        public bool BySystem => Caller.Kind == TenancyCallerKind.SystemInTenant;

        /// <summary>
        /// Whether the caller is held to containment: it is a seat, and the catalogue keeps the keys that manage
        /// access contained (<see cref="TenancyCatalogue.ContainAccessManagingKeys"/>). System work in a tenant
        /// holds every key there and never is, whatever the catalogue says.
        /// </summary>
        private bool HeldToContainment => Caller.Kind == TenancyCallerKind.Seat && _catalogue.ContainAccessManagingKeys;

        /// <summary>
        /// The keys of <paramref name="role"/> that a seat hands on only where it holds them: its keys that manage
        /// access while the catalogue keeps them contained, and none once the application turns that off, when
        /// the role is given and taken away as one that manages no access.
        /// </summary>
        private IReadOnlyList<string> ContainedKeysOf(TRole role)
            => _catalogue.ContainAccessManagingKeys ? _catalogue.AccessManagingKeysOf(role.Facts) : [];

        /// <summary>
        /// Whether a seat hands <paramref name="key"/> on only where it holds it: a key that manages access while
        /// the catalogue keeps such keys contained, and no key once the application turns that off.
        /// </summary>
        private bool IsContained(string key) => _catalogue.ContainAccessManagingKeys && _catalogue.ManagesAccess(key);

        /// <summary>
        /// Who the command's changes are recorded as, on every event it raises: the caller's own actor, a seat as
        /// itself and system work as the system, the operator or the token it was begun for. Every command passes
        /// this to the aggregate, and never an actor it worked out itself, so an event and the row of an event
        /// log it is kept in cannot come to disagree. <see langword="null"/> only for nobody, who is refused before
        /// anything changes.
        /// </summary>
        public TenancyActor<TSeatId>? By => Caller.Actor;

        /// <summary>The access questions for the caller, over the store's reads; made when first asked for.</summary>
        public ITenancyQuestions<TTenantId, TSeatId, TUnitId, TRoleId> Questions
            => _questions ??= new TenancyQuestions<TTenantId, TSeatId, TUnitId, TRoleId>(_store.Reads, _store.Queries, _catalogue, Caller, Now);

        /// <summary>The tenant a seat or system work in a tenant acts in. Reads nothing.</summary>
        /// <exception cref="Exceptions.RefusalException">The caller is nobody.</exception>
        /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
        public TTenantId RequireTenant()
            => Caller.Kind switch
            {
                TenancyCallerKind.Seat or TenancyCallerKind.SystemInTenant => Caller.Tenant!.Value,
                TenancyCallerKind.System => throw new InvalidOperationException(
                    "System work outside any tenant only provisions tenants. Begin TenancyWork.BeginSystemIn(tenant) for work inside one."),
                _ => throw Refusal(),
            };

        /// <summary>Only system work outside any tenant provisions one.</summary>
        /// <remarks>
        /// A seat is refused with the toolkit's <c>access.system-only</c>, the code a request that requires system
        /// work is refused with at the door, so a client reads one code for "only the application itself" wherever
        /// it is refused.
        /// </remarks>
        /// <exception cref="Exceptions.RefusalException"><c>access.system-only</c> for a seat; the caller's own refusal for nobody.</exception>
        /// <exception cref="InvalidOperationException">The caller is system work in a tenant.</exception>
        public void RequireSystemOutsideTenants()
        {
            switch (Caller.Kind)
            {
                case TenancyCallerKind.System:
                    return;
                case TenancyCallerKind.Seat:
                    throw ToolkitRefusals.Of(ToolkitRefusals.SystemOnly);
                case TenancyCallerKind.SystemInTenant:
                    throw new InvalidOperationException(
                        "A tenant is provisioned by system work outside any tenant. Begin TenancyWork.BeginSystem() for it.");
                default:
                    throw Refusal();
            }
        }

        /// <summary>
        /// Only system work in the tenant does <paramref name="work"/>: suspending, reactivating or closing it, or making
        /// its roles follow their packs.
        /// </summary>
        /// <param name="work">What the use case does, as the start of a sentence that ends "by system work in that tenant".</param>
        /// <exception cref="Exceptions.RefusalException"><c>access.system-only</c> for a seat; the caller's own refusal for nobody.</exception>
        /// <exception cref="InvalidOperationException">The caller is system work outside any tenant, told what to begin for <paramref name="work"/>.</exception>
        public TTenantId RequireSystemInTenant(string work)
            => Caller.Kind switch
            {
                TenancyCallerKind.SystemInTenant => Caller.Tenant!.Value,
                TenancyCallerKind.Seat => throw ToolkitRefusals.Of(ToolkitRefusals.SystemOnly),
                TenancyCallerKind.System => throw new InvalidOperationException(
                    $"{work} by system work in that tenant. Begin TenancyWork.BeginSystemIn(tenant) for it."),
                _ => throw Refusal(),
            };

        /// <summary>
        /// Takes the tenant's access revision. A command that changes rights, or what they reach, calls this
        /// before any other read of the store.
        /// </summary>
        public Task SerializeAsync(TTenantId tenant, CancellationToken cancellationToken)
            => _store.SerializeAccessChangesAsync(tenant, cancellationToken);

        /// <summary>A seat must hold <paramref name="key"/> for the whole tenant; system work in a tenant holds it.</summary>
        /// <exception cref="Exceptions.RefusalException"><c>tenancy.not-permitted</c>.</exception>
        public async Task RequireTenantWideAsync(string key, CancellationToken cancellationToken)
        {
            RequireTenant();
            if (BySystem)
            {
                return;
            }

            if (!await Questions.HoldsTenantWideAsync(key, cancellationToken).ConfigureAwait(false))
            {
                throw NotPermitted(key, unit: null);
            }
        }

        /// <summary>A seat must hold <paramref name="key"/> at <paramref name="unit"/>, there or above it; system work in a tenant holds it.</summary>
        /// <exception cref="Exceptions.RefusalException"><c>tenancy.not-permitted</c>.</exception>
        public async Task RequireAtAsync(string key, TUnitId unit, CancellationToken cancellationToken)
        {
            RequireTenant();
            if (BySystem)
            {
                return;
            }

            if (!await Questions.HoldsAtAsync(key, unit, cancellationToken).ConfigureAwait(false))
            {
                throw NotPermitted(key, unit);
            }
        }

        /// <summary>
        /// Whether a seat may give <paramref name="role"/> at <paramref name="unit"/>, where it holds
        /// <see cref="TenancyKeys.GrantsManage"/> already. Holding that key is enough to give roles: a role that
        /// manages no access goes to anyone placed there, for as long as the seat says, without the seat holding
        /// its keys; to the seat itself only for as long as it holds <see cref="TenancyKeys.GrantsManage"/>
        /// there. A role that manages access goes only from a seat that holds each of its keys that do at that
        /// unit, for at least as long as the grant runs, and never to the seat itself, whatever it holds.
        /// Otherwise a seat holding a key for a week could hand it out for good, and outlast its own grant through
        /// another seat. System work in a tenant is not held to this. With containment off every role goes as one
        /// that manages no access: to anyone placed there for as long as the seat says, and to the seat itself for
        /// no longer than it holds <see cref="TenancyKeys.GrantsManage"/> there.
        /// </summary>
        /// <param name="to">
        /// The seat the role goes to, or <see langword="null"/> for a seat still to be made, which cannot be the
        /// caller.
        /// </param>
        /// <param name="unit">Where.</param>
        /// <param name="role">The role, as it is now.</param>
        /// <param name="until">When the grant would end, or <see langword="null"/> for no end.</param>
        /// <param name="cancellationToken">Cancels the query.</param>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.self-appointment</c> for a role that manages access given to the caller itself, while
        /// containment is on; <c>tenancy.grant-exceeds-own</c>, naming the keys the seat lacks there, or lacks for
        /// long enough.
        /// </exception>
        public async Task RequireGrantableAsync(TSeatId? to, TUnitId unit, TRole role, DateTimeOffset? until, CancellationToken cancellationToken)
        {
            if (Caller.Kind != TenancyCallerKind.Seat)
            {
                return;
            }

            var managing = ContainedKeysOf(role);
            var self = to is { } target && target.Equals(Caller.Seat!.Value);
            if (managing.Count > 0 && self)
            {
                throw TenancyRefusals.Of(TenancyRefusals.SelfAppointment, ("Role", role.Id));
            }

            if (managing.Count == 0 && !self)
            {
                return;
            }

            var held = await HeldUntilAsync(unit, cancellationToken).ConfigureAwait(false);
            RefuseShort(managing.Count > 0 ? managing : [TenancyKeys.GrantsManage], held, until, role.Id);
        }

        /// <summary>
        /// Whether a seat may take away, at <paramref name="unit"/>, the grants of these roles, where it holds
        /// <see cref="TenancyKeys.GrantsManage"/> already. A role that manages no access goes without any of its
        /// keys. For one that manages access the seat holds each of its keys that do at that unit, until at least
        /// the end of that grant, or for good for a grant with no end: taking a role away is as contained as
        /// giving it. A seat's own grant that applies now is its own hold, so a seat takes its current roles away;
        /// one that has ended, or is still to start, holds nothing. System work in a tenant is not held to this, and
        /// with containment off no seat is either.
        /// </summary>
        /// <param name="grants">The roles, as they are now, each with the end of its grant there.</param>
        /// <param name="unit">Where.</param>
        /// <param name="cancellationToken">Cancels the query.</param>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.grant-exceeds-own</c>, for the first role whose keys the seat lacks there, or lacks for long enough.
        /// </exception>
        public async Task RequireRevocableAsync(IReadOnlyList<(TRole Role, DateTimeOffset? Until)> grants, TUnitId unit, CancellationToken cancellationToken)
        {
            if (!HeldToContainment)
            {
                return;
            }

            var contained = grants
                .Select(grant => (grant.Role, grant.Until, Keys: _catalogue.AccessManagingKeysOf(grant.Role.Facts)))
                .Where(grant => grant.Keys.Count > 0)
                .ToArray();
            if (contained.Length == 0)
            {
                return;
            }

            var held = await HeldUntilAsync(unit, cancellationToken).ConfigureAwait(false);
            foreach (var (role, until, keys) in contained)
            {
                RefuseShort(keys, held, until, role.Id);
            }
        }

        /// <summary>
        /// Whether a seat may suspend, deactivate or reactivate <paramref name="seat"/>, where it holds
        /// <see cref="TenancyKeys.SeatsManage"/> for the whole tenant already. A seat's status decides whether its
        /// grants count, so the change takes away, or gives back, every grant of it that has not ended, and each
        /// one of a role that manages access is held to the rule for taking it away
        /// (<see cref="RequireRevocableAsync"/>): at its own unit, until at least its own end. A grant that has
        /// ended gives nothing either way, and is not counted. A seat's own grant that applies now is its own
        /// hold, so a seat may suspend or deactivate itself; it never reactivates itself, because a
        /// suspended seat acts as nobody. System work in a tenant is not held to this, and with containment off no
        /// seat is either: the seats key for the whole tenant, which the use case asks, is all it takes.
        /// </summary>
        /// <param name="seat">The seat whose status would change, as it is now.</param>
        /// <param name="cancellationToken">Cancels the queries.</param>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.grant-exceeds-own</c>, for the first role whose keys the seat lacks at that grant's unit, or
        /// lacks for long enough.
        /// </exception>
        public async Task RequireStatusChangeableAsync(TSeat seat, CancellationToken cancellationToken)
        {
            if (!HeldToContainment)
            {
                return;
            }

            foreach (var placement in seat.Placements)
            {
                var grants = new List<(TRole Role, DateTimeOffset? Until)>();
                foreach (var grant in placement.Grants)
                {
                    if (grant.EndsAt is { } end && end <= Now)
                    {
                        continue;
                    }

                    if (await _store.FindRoleAsync(grant.RoleId, cancellationToken).ConfigureAwait(false) is { } role)
                    {
                        grants.Add((role, grant.EndsAt));
                    }
                }

                await RequireRevocableAsync(grants, placement.UnitId, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// An administrator alone, a seat holding <see cref="TenancyKeys.AdministratorKey"/> at the root with no
        /// end, changes which keys that manage access a role holds: by adding one, which reaches every seat that
        /// holds the role, seats that gave it to themselves included, or by taking one out or archiving the role,
        /// which takes it from every holder at once. So a seat holding the key for a week neither gives lasting
        /// power over access that way, nor takes away what it could not revoke grant by grant. System work in a
        /// tenant is not held to this, and with containment off no seat is either: the roles key for the whole
        /// tenant, which every change of a role asks, is all it takes.
        /// </summary>
        /// <param name="tenant">The tenant.</param>
        /// <param name="role">The role that would change, for the refusal.</param>
        /// <param name="cancellationToken">Cancels the queries.</param>
        /// <exception cref="Exceptions.RefusalException"><c>tenancy.grant-exceeds-own</c>, naming <see cref="TenancyKeys.AdministratorKey"/>.</exception>
        public async Task RequireAdministratorAsync(TTenantId tenant, TRoleId role, CancellationToken cancellationToken)
        {
            if (!HeldToContainment)
            {
                return;
            }

            var held = await RootAsync(tenant, cancellationToken).ConfigureAwait(false) is { } root
                ? await HeldUntilAsync(root, cancellationToken).ConfigureAwait(false)
                : new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal);
            RefuseShort([TenancyKeys.AdministratorKey], held, until: null, role);
        }

        /// <summary>
        /// Whether a seat may move <paramref name="unit"/> from under <paramref name="parent"/> to under
        /// <paramref name="newParent"/>, where it holds <see cref="TenancyKeys.UnitsManage"/> at both already. A
        /// move changes what reaches the unit and everything below it: the grants at the units above the parent
        /// that are not above the new parent stop reaching it, and those above the new parent that are not above
        /// the parent start to. So a move is held to the rules for giving and taking away, over what it changes.
        /// It gives the mover nothing: every key the mover's grants there would give it at the unit, it holds at
        /// the unit already, for at least as long, or for good when they give it for good. And it gives or takes
        /// away no key that manages access, from anyone, that the mover does not hold at the unit until at least
        /// the end of the grant that gives it, or for good for a grant with no end. A grant still to start
        /// counts; one that has ended gives nothing either way. Keys that manage no access follow the move freely
        /// for everyone but the mover, as a role holding only them is given freely. Otherwise a seat that manages
        /// units in two parts of the tree could move a unit where it, or someone else, holds more, or holds the
        /// same for longer, and so give what nobody could have given, or take what nobody could have taken away.
        /// System work in a tenant is not held to this. With containment off every key follows a move as one that
        /// manages no access does, freely for everyone but the mover: the move still gives the mover nothing.
        /// <para>
        /// An administrator whose role holds every live key passes both parts wherever a unit goes. One whose pack
        /// lists its keys holds every key that manages access at the root, and so passes the second part; the
        /// first still counts a grant of its own below the root that gives it a key its role at the root lacks.
        /// </para>
        /// </summary>
        /// <param name="unit">The unit that would move.</param>
        /// <param name="parent">The unit it hangs under now.</param>
        /// <param name="newParent">The unit it would hang under.</param>
        /// <param name="cancellationToken">Cancels the queries.</param>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.grant-exceeds-own</c>, naming the keys the move would give or take away that the seat lacks
        /// at the unit, or lacks for long enough.
        /// </exception>
        public async Task RequireMovableAsync(TUnitId unit, TUnitId parent, TUnitId newParent, CancellationToken cancellationToken)
        {
            if (Caller.Kind != TenancyCallerKind.Seat)
            {
                return;
            }

            // Every grant that has not ended, of the mover's or of a key that manages access, at a parent or a
            // unit above one, once for each parent it reaches. The move changes nothing that reaches from a unit
            // above both parents: the mover's own come back twice from there, and another seat's not at all. These
            // are other seats' rights too, so the store answers, which reads them where a seat's own reads may not.
            var rows = await _store.RightsAMoveChangesAsync(
                Caller.Tenant!.Value, Caller.Seat!.Value, parent, newParent, _catalogue.AccessManagingKeys, Now, cancellationToken).ConfigureAwait(false);
            var aboveParent = rows.Where(row => row.Parent.Equals(parent)).Select(row => row.UnitId).ToHashSet();
            var aboveNewParent = rows.Where(row => row.Parent.Equals(newParent)).Select(row => row.UnitId).ToHashSet();
            var changed = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var gives = row.Parent.Equals(newParent) && !aboveParent.Contains(row.UnitId);
                var takes = row.Parent.Equals(parent) && !aboveNewParent.Contains(row.UnitId);
                var counts = IsContained(row.Key)
                    ? gives || takes
                    : gives && row.OfCaller && _catalogue.IsLive(row.Key);
                if (counts)
                {
                    Extend(changed, row.Key, row.EndsAt);
                }
            }

            if (changed.Count == 0)
            {
                return;
            }

            var held = await HeldUntilAsync(unit, cancellationToken).ConfigureAwait(false);
            var missing = changed
                .Where(pair => !HeldLongEnough(held, pair.Key, pair.Value))
                .Select(pair => pair.Key)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (missing.Length > 0)
            {
                throw TenancyRefusals.Of(TenancyRefusals.GrantExceedsOwn, ("Role", (TRoleId?)null), ("Missing", string.Join(", ", missing)));
            }
        }

        /// <summary>
        /// Refuses a change that would leave a tenant that has an administrator without one, before anything is
        /// changed: a refused command leaves nothing behind for a later save in the same unit of work to write.
        /// An administrator is an active seat, placed at the root, holding there an active role with the
        /// administrator key, granted with no end and applying now.
        /// <para>
        /// The administrators as last saved are the rights at the root that make one, which only active seats and
        /// active roles have, as (seat, role) pairs: the store answers them, since they are other seats' rights
        /// too. The pairs the command is about to take away are dropped, and if none is left of a tenant that had
        /// some, the command is refused. Two such commands racing each other are kept apart by the access
        /// revision, which each took first.
        /// </para>
        /// </summary>
        /// <param name="tenant">The tenant.</param>
        /// <param name="loss">What the command is about to take away.</param>
        /// <param name="cancellationToken">Cancels the queries.</param>
        /// <exception cref="Exceptions.RefusalException"><c>tenancy.last-admin</c>.</exception>
        public async Task EnsureAdministratorRemainsAsync(TTenantId tenant, AdministratorLoss loss, CancellationToken cancellationToken)
        {
            var root = await RootAsync(tenant, cancellationToken).ConfigureAwait(false);
            if (root is not { } rootId)
            {
                return;
            }

            if (loss.AtUnit is { } at && !at.Equals(rootId))
            {
                // Only what is held at the root makes an administrator.
                return;
            }

            var pairs = await _store.AdministratorsAsync(tenant, Now, cancellationToken).ConfigureAwait(false);
            if (pairs.Count > 0 && pairs.All(pair => loss.Takes(pair.Seat, pair.Role)))
            {
                throw TenancyRefusals.Of(TenancyRefusals.LastAdmin);
            }
        }

        /// <summary>
        /// Who is recorded as having placed or granted: the calling seat, or the seat system work acts for.
        /// System work may act on the very seat it acts for, which a seat may not do itself when placing, or
        /// when giving a role that manages access; it then records nobody. That is the seat a placement or a
        /// grant keeps, and what the seat's own rules read; who acted, in full, is <see cref="By"/>.
        /// </summary>
        public TSeatId? ActorFor(TSeatId target)
            => BySystem && Caller.Seat is { } acting && acting.Equals(target) ? null : Caller.Seat;

        /// <summary>The tenant, which is the caller's own.</summary>
        public async Task<TTenant> LoadTenantAsync(TTenantId id, CancellationToken cancellationToken)
            => await _store.FindTenantAsync(id, cancellationToken).ConfigureAwait(false) ?? throw Missing("tenant", id);

        /// <summary>The tenant's organization, which is the caller's own.</summary>
        public async Task<TOrganization> LoadOrganizationAsync(TTenantId id, CancellationToken cancellationToken)
            => await _store.FindOrganizationAsync(id, cancellationToken).ConfigureAwait(false) ?? throw Missing("organization", id);

        /// <summary>
        /// The tenant's organization, which is the caller's own, read only: for the directory, which hands its units
        /// to the application and saves nothing of them.
        /// </summary>
        public async Task<TOrganization> ReadOrganizationAsync(TTenantId id, CancellationToken cancellationToken)
            => await _store.ReadOrganizationAsync(id, cancellationToken).ConfigureAwait(false) ?? throw Missing("organization", id);

        /// <summary>A seat of the caller's tenant.</summary>
        /// <exception cref="Exceptions.RefusalException"><c>tenancy.seat-not-found</c>, for a seat of another tenant too.</exception>
        public async Task<TSeat> LoadSeatAsync(TSeatId id, CancellationToken cancellationToken)
            => await _store.FindSeatAsync(id, cancellationToken).ConfigureAwait(false) ?? throw TenancyRefusals.Of(TenancyRefusals.SeatNotFound);

        /// <summary>A role of the caller's tenant.</summary>
        /// <exception cref="Exceptions.RefusalException"><c>tenancy.role-not-found</c>, for a role of another tenant too.</exception>
        public async Task<TRole> LoadRoleAsync(TRoleId id, CancellationToken cancellationToken)
            => await _store.FindRoleAsync(id, cancellationToken).ConfigureAwait(false) ?? throw TenancyRefusals.Of(TenancyRefusals.RoleNotFound);

        /// <summary>The tenant's root, or <see langword="null"/> when it has no units to read.</summary>
        private Task<TUnitId?> RootAsync(TTenantId tenant, CancellationToken cancellationToken)
            => _store.Queries.FirstOrDefaultAsync(
                _store.Reads.Units
                    .Where(unit => unit.TenantId.Equals(tenant) && !unit.ParentId.HasValue)
                    .Select(unit => (TUnitId?)unit.Id),
                cancellationToken);

        /// <summary>
        /// Every key the calling seat holds now at <paramref name="unit"/>, granted there or above it, with how
        /// long it holds it: the latest end of the grants that give it, or <see langword="null"/> when one of them
        /// has none. One query.
        /// </summary>
        private async Task<Dictionary<string, DateTimeOffset?>> HeldUntilAsync(TUnitId unit, CancellationToken cancellationToken)
        {
            var tenant = Caller.Tenant!.Value;
            var seat = Caller.Seat!.Value;
            var now = Now;
            var reaching = from right in _store.Reads.SeatRights
                           where right.TenantId.Equals(tenant) && right.SeatId.Equals(seat)
                                 && right.StartsAt <= now && (right.EndsAt == null || right.EndsAt > now)
                           join path in _store.Reads.UnitPaths on right.UnitId equals path.AncestorId
                           where path.TenantId.Equals(tenant) && path.DescendantId.Equals(unit)
                           select new { right.Key, right.EndsAt };

            var rows = await _store.Queries.ListAsync(reaching, cancellationToken).ConfigureAwait(false);
            var held = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                Extend(held, row.Key, row.EndsAt);
            }

            return held;
        }

        /// <summary>
        /// Records in <paramref name="until"/> that <paramref name="key"/> runs until <paramref name="end"/>, or for
        /// good when <paramref name="end"/> is <see langword="null"/>, keeping the latest end a key has there.
        /// </summary>
        private static void Extend(Dictionary<string, DateTimeOffset?> until, string key, DateTimeOffset? end)
        {
            if (!until.TryGetValue(key, out var known))
            {
                until[key] = end;
            }
            else if (known is { } ends && (end is not { } other || other > ends))
            {
                until[key] = end;
            }
        }

        /// <summary>
        /// Refuses with the keys of <paramref name="wanted"/> the seat lacks: not held, or held until an end
        /// earlier than <paramref name="until"/>, or until any end when the grant would have none.
        /// </summary>
        private static void RefuseShort(IReadOnlyList<string> wanted, IReadOnlyDictionary<string, DateTimeOffset?> held, DateTimeOffset? until, TRoleId role)
        {
            var missing = wanted
                .Where(key => !HeldLongEnough(held, key, until))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (missing.Length > 0)
            {
                throw TenancyRefusals.Of(TenancyRefusals.GrantExceedsOwn, ("Role", role), ("Missing", string.Join(", ", missing)));
            }
        }

        /// <summary>
        /// Whether <paramref name="key"/> is in <paramref name="held"/> until <paramref name="until"/> or later: for
        /// good, or until an end no earlier than it when <paramref name="until"/> has one.
        /// </summary>
        private static bool HeldLongEnough(IReadOnlyDictionary<string, DateTimeOffset?> held, string key, DateTimeOffset? until)
            => held.TryGetValue(key, out var end) && (end is not { } ends || (until is { } needed && ends >= needed));

        private Exceptions.RefusalException Refusal() => TenancyRefusals.Of(Caller.Refusal ?? TenancyRefusals.NotSeated);

        /// <summary>
        /// <c>tenancy.not-permitted</c>, with the key and the unit as values. The unit is not written into the
        /// message: an argument that carried a piece of formatting would be text, not a value.
        /// </summary>
        private static Exceptions.RefusalException NotPermitted(string key, TUnitId? unit)
            => TenancyRefusals.Of(TenancyRefusals.NotPermitted, ("Key", key), ("Unit", unit));

        private static InvalidOperationException Missing(string what, TTenantId id)
            => new("The " + what + " " + id + " of the current caller is not found. A seat's tenant always is, so the caller was begun with a tenant that does not exist.");
    }

    /// <summary>
    /// What a command is about to take from the tenant's administrators, as the (seat, role) pairs at the root
    /// it takes: every pair of a seat, every pair of a role, or one seat's pair of one role; and only when the
    /// change is at the root, where a unit is named.
    /// </summary>
    internal readonly record struct AdministratorLoss
    {
        private AdministratorLoss(TSeatId? seat, TRoleId? role, TUnitId? atUnit)
        {
            Seat = seat;
            Role = role;
            AtUnit = atUnit;
        }

        /// <summary>The seat whose pairs go, or <see langword="null"/> for every seat's.</summary>
        public TSeatId? Seat { get; }

        /// <summary>The role whose pairs go, or <see langword="null"/> for every role's.</summary>
        public TRoleId? Role { get; }

        /// <summary>The unit the change is at, or <see langword="null"/> when it is not at one unit.</summary>
        public TUnitId? AtUnit { get; }

        /// <summary>A seat that stops counting: suspended or deactivated.</summary>
        public static AdministratorLoss OfSeat(TSeatId seat) => new(seat, null, null);

        /// <summary>A seat withdrawn from a unit, with every grant it held there.</summary>
        public static AdministratorLoss OfPlacement(TSeatId seat, TUnitId unit) => new(seat, null, unit);

        /// <summary>One role revoked from a seat at a unit.</summary>
        public static AdministratorLoss OfGrant(TSeatId seat, TUnitId unit, TRoleId role) => new(seat, role, unit);

        /// <summary>A role that stops administering: archived, or without the administrator key from now on.</summary>
        public static AdministratorLoss OfRole(TRoleId role) => new(null, role, null);

        /// <summary>Whether the pair of <paramref name="seat"/> and <paramref name="role"/> goes.</summary>
        public bool Takes(TSeatId seat, TRoleId role)
            => (Seat is not { } lost || lost.Equals(seat)) && (Role is not { } gone || gone.Equals(role));
    }
}
