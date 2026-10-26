using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// Writes the rows the access questions read in the save that changes the aggregates they come from: the
/// closure of every organization whose tree changed, and the rights of every seat a change reaches. What
/// changed is read from the change tracker, so no use case has to say so, and the rows are written in the
/// save's own transaction, so a save that fails writes none of them.
/// <list type="bullet">
/// <item>A seat that was added, or whose status or grants changed, has its rights written again in full,
/// from the seat in memory.</item>
/// <item>A role that gains or loses keys, or is archived, has the rows it gives rewritten for each seat granted
/// it. Those seats are not loaded: one query reads the grants of that role, and the rest of each seat's rows
/// stay as they are, since nothing else about the seat changed.</item>
/// <item>An organization that was added, or whose units were added, removed or moved, has its closure
/// written again. Renaming or archiving a unit changes no pair of units, so it writes nothing.</item>
/// <item>Two columns the aggregates do not hold are filled in: a placement's tenant, which the placements' view
/// is filtered on, and a role's normalized name, which keeps a tenant's role names unique ignoring case.</item>
/// </list>
/// <para>
/// Its reads look past every query filter, the application's as well as the tenant's: a row an application's
/// filter hides still holds rights, or still gives them, so leaving it out would rewrite them as if it were
/// gone. Each read is kept to the keys of what changed. The
/// rows it adds are not checked against the caller's tenant: they are worked out from the rows the save check
/// has just passed. A row that already holds the right values is left alone, so a save that changes nothing
/// about access writes nothing here.
/// </para>
/// <para>
/// Where the database keeps the rights itself (<see cref="TenancyStoreOptions.DatabaseKeepsRights"/>), on a
/// relational database, the rights are left to it: the save writes the closure and the two columns, and no
/// rights row. Such a database writes them as the grant, the seat or the role is written, and would show a
/// seat's save none of another seat's rows to bring up to date.
/// </para>
/// </summary>
/// <param name="catalogue">Which keys are live: a key that is not holds nowhere, and gets no row.</param>
/// <param name="store">What the store leaves to the database.</param>
internal sealed class TenancyProjectionWriter<TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>(TenancyCatalogue catalogue, TenancyStoreOptions store)
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>
    /// Adds, changes and removes the closure and rights rows the save's changes call for. Does nothing in a
    /// context that does not keep Tenancy's seats: every context that keeps entities to a tenant shares the
    /// one interceptor, a consumer's included.
    /// </summary>
    /// <param name="context">The context being saved, after the save check.</param>
    /// <param name="async">Whether to read asynchronously; the synchronous save reads synchronously.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task WriteAsync(DbContext context, bool async, CancellationToken cancellationToken)
    {
        if (context.Model.FindEntityType(typeof(TSeat)) is null)
        {
            return;
        }

        // The save check has just detected the changes. The writer walks the tracker several times, and what it
        // changes itself it marks as it goes, so detecting them again for every walk would only cost time.
        var tracker = context.ChangeTracker;
        var detect = tracker.AutoDetectChangesEnabled;
        tracker.AutoDetectChangesEnabled = false;
        try
        {
            var changes = Changes.Of(tracker);
            FillDerivedColumns(changes);
            await WriteClosuresAsync(context, changes, async, cancellationToken).ConfigureAwait(false);
            if (!(store.DatabaseKeepsRights && context.Database.IsRelational()))
            {
                await WriteRightsAsync(context, changes, async, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = detect;
        }
    }

    /// <summary>
    /// A new or changed placement gets its seat's tenant, and a new or changed role its normalized name. The seat
    /// that owns a placement is tracked whenever the placement is; the save check has made sure of it.
    /// </summary>
    private static void FillDerivedColumns(Changes changes)
    {
        foreach (var placement in changes.WrittenPlacements)
        {
            var seat = (TSeatId)placement.Property(TenancyMapping.SeatColumn).CurrentValue!;
            var owner = changes.Seats.TryGetValue(seat, out var entry)
                ? (TSeat)entry.Entity
                : throw new InvalidOperationException("A placement of seat " + seat + " is saved without its seat.");

            SetIfChanged(placement.Property(TenancyMapping.TenantColumn), owner.TenantId);
        }

        foreach (var role in changes.WrittenRoles)
        {
            SetIfChanged(role.Property(TenancyMapping.NormalizedNameColumn), TenancyMapping.NormalizedRoleName(((TRole)role.Entity).Name));
        }
    }

    /// <summary>The closure of every organization whose tree changed, written again.</summary>
    private static async Task WriteClosuresAsync(DbContext context, Changes changes, bool async, CancellationToken cancellationToken)
    {
        // A unit is part of its organization, so the organization is tracked whenever a unit is; one that were
        // not would be left alone rather than lose its closure.
        var organizations = changes.ChangedOrganizations.Where(changes.Organizations.ContainsKey).ToHashSet();
        if (organizations.Count == 0)
        {
            return;
        }

        var ids = organizations.ToArray();
        await LoadAsync(
            context.Set<OrganizationUnitPath<TTenantId, TUnitId>>()
                .IgnoreQueryFilters()
                .Where(path => ids.Contains(path.TenantId)),
            async,
            cancellationToken).ConfigureAwait(false);

        var desired = new List<OrganizationUnitPath<TTenantId, TUnitId>>();
        foreach (var id in ids)
        {
            var entry = changes.Organizations[id];
            if (entry.State != EntityState.Deleted)
            {
                desired.AddRange(TenancyProjection.ClosureOf((TOrganization)entry.Entity));
            }
        }

        Apply(
            context,
            desired,
            path => organizations.Contains(path.TenantId),
            path => (path.AncestorId, path.DescendantId),
            static (entry, path) => SetIfChanged(entry.Property(row => row.Distance), path.Distance));
    }

    /// <summary>
    /// The rights of every seat that changed, in full, and of every seat that holds a changed role, for that
    /// role, written again.
    /// </summary>
    private async Task WriteRightsAsync(DbContext context, Changes changes, bool async, CancellationToken cancellationToken)
    {
        // A grant is part of its seat, so the seat is tracked whenever a grant is; one that were not would be
        // left alone rather than lose its rights.
        var seats = changes.ChangedSeats.Where(changes.Seats.ContainsKey).ToHashSet();
        var roles = changes.ChangedRoles;
        if (seats.Count == 0 && roles.Count == 0)
        {
            return;
        }

        var grants = new Dictionary<TSeatId, SeatGrants>();
        foreach (var id in seats)
        {
            var entry = changes.Seats[id];
            if (entry.State != EntityState.Deleted)
            {
                var seat = (TSeat)entry.Entity;
                grants[id] = new SeatGrants(seat.TenantId, seat.Status, [.. TenancyProjection.GrantsOf(seat)]);
            }
        }

        if (roles.Count > 0)
        {
            var changedRoles = roles.ToArray();
            foreach (var held in await ListAsync(HoldersOf(context, changedRoles), async, cancellationToken).ConfigureAwait(false))
            {
                if (seats.Contains(held.SeatId))
                {
                    continue;
                }

                if (!grants.TryGetValue(held.SeatId, out var holder))
                {
                    grants[held.SeatId] = holder = new SeatGrants(held.TenantId, held.Status, []);
                }

                holder.Grants.Add(new GrantFact<TUnitId, TRoleId>(held.UnitId, held.RoleId, held.StartsAt, held.EndsAt));
            }
        }

        var facts = await RoleFactsAsync(
            context,
            changes,
            grants.Values.SelectMany(seat => seat.Grants.Select(grant => grant.RoleId)).Concat(roles),
            async,
            cancellationToken).ConfigureAwait(false);

        var desired = new List<SeatRight<TTenantId, TSeatId, TUnitId, TRoleId>>();
        foreach (var (id, seat) in grants)
        {
            desired.AddRange(TenancyProjection.RightsOf(
                seat.Tenant,
                id,
                seat.Status,
                seat.Grants,
                role => facts.TryGetValue(role, out var known) && known.Tenant.Equals(seat.Tenant) ? known.Facts : null,
                catalogue));
        }

        await LoadAsync(StoredRights(context, [.. seats], [.. roles]), async, cancellationToken).ConfigureAwait(false);

        // In scope: every row of a changed seat, and the rows of a changed role of any seat. The rows written
        // for a holder are of the changed roles only, which is exactly its part of that scope.
        Apply(
            context,
            desired,
            right => seats.Contains(right.SeatId) || roles.Contains(right.RoleId),
            right => (right.SeatId, right.UnitId, right.RoleId, right.Key),
            static (entry, right) =>
            {
                SetIfChanged(entry.Property(row => row.TenantId), right.TenantId);
                SetIfChanged(entry.Property(row => row.StartsAt), right.StartsAt);
                SetIfChanged(entry.Property(row => row.EndsAt), right.EndsAt);
            });
    }

    /// <summary>
    /// What is known of each role: from the role in memory when it is tracked, since it may be changing in this
    /// very save, and otherwise its status and keys as stored, read in one query. A role that is deleted, or
    /// found nowhere, is known as nothing.
    /// </summary>
    private static async Task<Dictionary<TRoleId, KnownRole>> RoleFactsAsync(
        DbContext context,
        Changes changes,
        IEnumerable<TRoleId> ids,
        bool async,
        CancellationToken cancellationToken)
    {
        var facts = new Dictionary<TRoleId, KnownRole>();
        var stored = new HashSet<TRoleId>();
        foreach (var id in ids)
        {
            if (facts.ContainsKey(id) || stored.Contains(id))
            {
                continue;
            }

            if (changes.Roles.TryGetValue(id, out var entry))
            {
                var role = (TRole)entry.Entity;
                facts[id] = new KnownRole(role.TenantId, entry.State == EntityState.Deleted ? null : role.Facts);
            }
            else
            {
                stored.Add(id);
            }
        }

        if (stored.Count > 0)
        {
            var wanted = stored.ToArray();
            var rows = await ListAsync(
                context.Set<TRole>()
                    .IgnoreQueryFilters()
                    .Where(role => wanted.Contains(role.Id))
                    .Select(role => new StoredRole(role.Id, role.TenantId, role.Status, role.Keys)),
                async,
                cancellationToken).ConfigureAwait(false);

            foreach (var row in rows)
            {
                facts[row.Id] = new KnownRole(row.TenantId, new RoleFacts(row.Status == RoleStatus.Active, row.Keys));
            }
        }

        return facts;
    }

    /// <summary>
    /// Every grant of <paramref name="roles"/>, with its seat's tenant and status, read from the seats' tables
    /// without loading a seat: the seats that hold a changed role.
    /// </summary>
    private static IQueryable<HeldGrant> HoldersOf(DbContext context, TRoleId[] roles)
        => from seat in context.Set<TSeat>().IgnoreQueryFilters()
           from placement in seat.Placements
           from grant in placement.Grants
           where roles.Contains(grant.RoleId)
           select new HeldGrant(seat.Id, seat.TenantId, seat.Status, placement.UnitId, grant.RoleId, grant.StartsAt, grant.EndsAt);

    /// <summary>The stored rights rows of <paramref name="seats"/>, and those of <paramref name="roles"/> of any seat.</summary>
    private static IQueryable<SeatRight<TTenantId, TSeatId, TUnitId, TRoleId>> StoredRights(DbContext context, TSeatId[] seats, TRoleId[] roles)
    {
        var rights = context.Set<SeatRight<TTenantId, TSeatId, TUnitId, TRoleId>>().IgnoreQueryFilters();
        return (seats.Length, roles.Length) switch
        {
            (> 0, > 0) => rights.Where(right => seats.Contains(right.SeatId) || roles.Contains(right.RoleId)),
            (> 0, _) => rights.Where(right => seats.Contains(right.SeatId)),
            _ => rights.Where(right => roles.Contains(right.RoleId)),
        };
    }

    /// <summary>
    /// Makes the tracked rows in scope what <paramref name="desired"/> says: a row it names that exists is kept
    /// and brought up to date in place, one it names that does not is added, and one in scope it does not name
    /// is removed. The rows in scope must have been loaded first; the ones the context already tracked count too,
    /// so a row is never added next to a tracked one with the same key.
    /// </summary>
    private static void Apply<TRow, TKey>(
        DbContext context,
        IEnumerable<TRow> desired,
        Func<TRow, bool> inScope,
        Func<TRow, TKey> keyOf,
        Action<EntityEntry<TRow>, TRow> update)
        where TRow : class
        where TKey : notnull
    {
        var existing = new Dictionary<TKey, EntityEntry<TRow>>();
        foreach (var entry in context.ChangeTracker.Entries<TRow>())
        {
            if (inScope(entry.Entity))
            {
                existing[keyOf(entry.Entity)] = entry;
            }
        }

        foreach (var row in desired)
        {
            if (existing.Remove(keyOf(row), out var entry))
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Unchanged;
                }

                update(entry, row);
            }
            else
            {
                context.Add(row);
            }
        }

        foreach (var stale in existing.Values)
        {
            // A row added earlier in this unit of work was never written, so it is forgotten rather than deleted.
            stale.State = stale.State == EntityState.Added ? EntityState.Detached : EntityState.Deleted;
        }
    }

    private static void SetIfChanged<TRow, TValue>(PropertyEntry<TRow, TValue> property, TValue value)
        where TRow : class
    {
        if (!EqualityComparer<TValue>.Default.Equals(property.CurrentValue, value))
        {
            property.CurrentValue = value;
        }
    }

    /// <summary>The same, for a shadow property, which has no typed entry.</summary>
    private static void SetIfChanged(PropertyEntry property, object value)
    {
        if (!Equals(property.CurrentValue, value))
        {
            property.CurrentValue = value;
        }
    }

    private static async Task<List<T>> ListAsync<T>(IQueryable<T> query, bool async, CancellationToken cancellationToken)
        => async ? await query.ToListAsync(cancellationToken).ConfigureAwait(false) : query.ToList();

    private static async Task LoadAsync<T>(IQueryable<T> query, bool async, CancellationToken cancellationToken)
    {
        if (async)
        {
            await query.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            query.Load();
        }
    }

    /// <summary>What the save changed that access depends on, read from the change tracker once.</summary>
    private sealed class Changes
    {
        /// <summary>Every tracked seat, by id.</summary>
        public Dictionary<TSeatId, EntityEntry> Seats { get; } = [];

        /// <summary>Every tracked role, by id.</summary>
        public Dictionary<TRoleId, EntityEntry> Roles { get; } = [];

        /// <summary>Every tracked organization, by id.</summary>
        public Dictionary<TTenantId, EntityEntry> Organizations { get; } = [];

        /// <summary>Seats added or deleted, or whose status or grants changed.</summary>
        public HashSet<TSeatId> ChangedSeats { get; } = [];

        /// <summary>Roles deleted, or whose status or keys changed. A role added has no holder that is not in this save.</summary>
        public HashSet<TRoleId> ChangedRoles { get; } = [];

        /// <summary>Organizations added or deleted, or whose units were added, removed or moved.</summary>
        public HashSet<TTenantId> ChangedOrganizations { get; } = [];

        /// <summary>Placements the save adds or changes, whose tenant column is filled in from their seat.</summary>
        public List<EntityEntry> WrittenPlacements { get; } = [];

        /// <summary>Roles the save adds or changes, whose normalized name is filled in from their name.</summary>
        public List<EntityEntry> WrittenRoles { get; } = [];

        public static Changes Of(ChangeTracker tracker)
        {
            var changes = new Changes();
            foreach (var entry in tracker.Entries())
            {
                if (entry.State is EntityState.Added or EntityState.Modified)
                {
                    switch (entry.Entity)
                    {
                        case Placement<TSeatId, TUnitId, TRoleId>:
                            changes.WrittenPlacements.Add(entry);
                            break;
                        case TRole:
                            changes.WrittenRoles.Add(entry);
                            break;
                    }
                }

                switch (entry.Entity)
                {
                    case TSeat seat:
                        changes.Seats[seat.Id] = entry;
                        if (entry.State is EntityState.Added or EntityState.Deleted || IsModified(entry, nameof(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>.Status)))
                        {
                            changes.ChangedSeats.Add(seat.Id);
                        }

                        break;

                    case TRole role:
                        changes.Roles[role.Id] = entry;
                        if (entry.State == EntityState.Deleted
                            || IsModified(entry, nameof(RoleAggregate<TRoleId, TTenantId>.Status))
                            || IsModified(entry, nameof(RoleAggregate<TRoleId, TTenantId>.Keys)))
                        {
                            changes.ChangedRoles.Add(role.Id);
                        }

                        break;

                    case TOrganization organization:
                        changes.Organizations[organization.Id] = entry;
                        if (entry.State is EntityState.Added or EntityState.Deleted)
                        {
                            changes.ChangedOrganizations.Add(organization.Id);
                        }

                        break;

                    // Owned rows name their owner through the key they share with it.
                    case TUnit when entry.State is EntityState.Added or EntityState.Deleted
                                    || IsModified(entry, nameof(OrganizationUnitEntity<TUnitId>.ParentId)):
                        changes.ChangedOrganizations.Add((TTenantId)entry.Property(TenancyMapping.TenantColumn).CurrentValue!);
                        break;

                    case RoleGrant<TSeatId, TRoleId> when entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted:
                    case Placement<TSeatId, TUnitId, TRoleId> when entry.State == EntityState.Deleted:
                        changes.ChangedSeats.Add((TSeatId)entry.Property(TenancyMapping.SeatColumn).CurrentValue!);
                        break;
                }
            }

            return changes;
        }

        private static bool IsModified(EntityEntry entry, string property)
            => entry.State == EntityState.Modified && entry.Property(property).IsModified;
    }

    /// <summary>A seat's tenant, status and grants: what its rights are worked out from.</summary>
    private sealed record SeatGrants(TTenantId Tenant, SeatStatus Status, List<GrantFact<TUnitId, TRoleId>> Grants);

    /// <summary>A role's tenant, and what it grants, or <see langword="null"/> when it grants nothing.</summary>
    private sealed record KnownRole(TTenantId Tenant, RoleFacts? Facts);

    /// <summary>A grant as the holders' query reads it, with its seat's tenant and status.</summary>
    private sealed record HeldGrant(TSeatId SeatId, TTenantId TenantId, SeatStatus Status, TUnitId UnitId, TRoleId RoleId, DateTimeOffset StartsAt, DateTimeOffset? EndsAt);

    /// <summary>A role as the role query reads it: no more than the rights need.</summary>
    private sealed record StoredRole(TRoleId Id, TTenantId TenantId, RoleStatus Status, IReadOnlyList<string> Keys);
}
