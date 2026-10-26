using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// The access questions over one read source, for one caller at one moment.
/// <para>
/// Every query is plain LINQ over the source's sets, so a storage can translate it: ids are compared with
/// <c>Equals</c>, an empty answer is the source's own set filtered to nothing, and every set that has a
/// tenant is kept to the caller's tenant here too, next to whatever filter the storage applies.
/// </para>
/// </summary>
internal sealed class TenancyQuestions<TTenantId, TSeatId, TUnitId, TRoleId>(
    ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId> source,
    IQueryExecutor executor,
    TenancyCatalogue catalogue,
    TenancyCaller<TTenantId, TSeatId> caller,
    DateTimeOffset now)
    : ITenancyQuestions<TTenantId, TSeatId, TUnitId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    public IQueryable<TUnitId> UnitsWhereIHold(string key)
    {
        var kind = Kind();
        if (!Askable(key) || kind == TenancyCallerKind.Nobody)
        {
            return NoUnits();
        }

        var tenant = caller.Tenant!.Value;
        if (kind == TenancyCallerKind.SystemInTenant)
        {
            return source.Units.Where(unit => unit.TenantId.Equals(tenant)).Select(unit => unit.Id);
        }

        var seat = caller.Seat!.Value;
        var moment = now;
        return (from right in source.SeatRights
                where right.TenantId.Equals(tenant) && right.SeatId.Equals(seat) && right.Key == key
                      && right.StartsAt <= moment && (right.EndsAt == null || right.EndsAt > moment)
                join path in source.UnitPaths on right.UnitId equals path.AncestorId
                where path.TenantId.Equals(tenant)
                select path.DescendantId).Distinct();
    }

    public IQueryable<UnitKey<TUnitId>> WhereIHold(IReadOnlyCollection<string> keys)
    {
        var kind = Kind();
        var asked = LiveAmong(keys);
        if (asked.Length == 0 || kind == TenancyCallerKind.Nobody)
        {
            return source.Units.Where(_ => false).Select(unit => new UnitKey<TUnitId> { Unit = unit.Id, Key = string.Empty });
        }

        // A pair is made by setting its members, which a storage reads back as columns: a query composed over
        // the pairs, one that keeps a unit's or picks out the keys, stays one statement.
        var tenant = caller.Tenant!.Value;
        if (kind == TenancyCallerKind.SystemInTenant)
        {
            // Every key, at every unit of its tenant.
            return ForEach(asked, key => source.Units
                .Where(unit => unit.TenantId.Equals(tenant))
                .Select(unit => new UnitKey<TUnitId> { Unit = unit.Id, Key = key }));
        }

        var seat = caller.Seat!.Value;
        var moment = now;
        return (from right in source.SeatRights
                where right.TenantId.Equals(tenant) && right.SeatId.Equals(seat) && asked.Contains(right.Key)
                      && right.StartsAt <= moment && (right.EndsAt == null || right.EndsAt > moment)
                join path in source.UnitPaths on right.UnitId equals path.AncestorId
                where path.TenantId.Equals(tenant)
                select new UnitKey<TUnitId> { Unit = path.DescendantId, Key = right.Key }).Distinct();
    }

    public IQueryable<TUnitId> ReadableUnits()
    {
        var kind = Kind();
        if (kind == TenancyCallerKind.Nobody)
        {
            return NoUnits();
        }

        var tenant = caller.Tenant!.Value;
        if (kind == TenancyCallerKind.SystemInTenant)
        {
            return source.Units.Where(unit => unit.TenantId.Equals(tenant)).Select(unit => unit.Id);
        }

        var seat = caller.Seat!.Value;
        return (from placement in source.Placements
                where placement.SeatId.Equals(seat)
                join path in source.UnitPaths on placement.UnitId equals path.AncestorId
                where path.TenantId.Equals(tenant)
                select path.DescendantId).Distinct();
    }

    public IQueryable<TRoleId> RolesWithKey(string key)
    {
        var kind = Kind();
        if (!Askable(key) || kind == TenancyCallerKind.Nobody)
        {
            return source.Roles.Where(_ => false).Select(role => role.Id);
        }

        var tenant = caller.Tenant!.Value;
        return source.Roles
            .Where(role => role.TenantId.Equals(tenant) && role.Status == RoleStatus.Active && role.Keys.Contains(key))
            .Select(role => role.Id);
    }

    public IQueryable<TUnitId> UnitsUnder(TUnitId unit)
    {
        if (Kind() == TenancyCallerKind.Nobody)
        {
            return NoUnits();
        }

        var tenant = caller.Tenant!.Value;
        return source.UnitPaths
            .Where(path => path.TenantId.Equals(tenant) && path.AncestorId.Equals(unit))
            .Select(path => path.DescendantId);
    }

    public IQueryable<RoleWithKey<TRoleId>> RoleKeys(IReadOnlyCollection<string> keys)
    {
        var kind = Kind();
        var asked = LiveAmong(keys);
        if (asked.Length == 0 || kind == TenancyCallerKind.Nobody)
        {
            return source.Roles.Where(_ => false).Select(role => new RoleWithKey<TRoleId> { Role = role.Id, Key = string.Empty });
        }

        // The roles that grant a key, for each key: a role's keys are one column, which not every storage can
        // open into rows, while every one can ask whether a key is among them.
        var tenant = caller.Tenant!.Value;
        return ForEach(asked, key => source.Roles
            .Where(role => role.TenantId.Equals(tenant) && role.Status == RoleStatus.Active && role.Keys.Contains(key))
            .Select(role => new RoleWithKey<TRoleId> { Role = role.Id, Key = key }));
    }

    public IQueryable<string> KeysIHoldAt(TUnitId unit)
    {
        var kind = Kind();
        if (kind == TenancyCallerKind.Nobody)
        {
            return source.SeatRights.Where(_ => false).Select(right => right.Key);
        }

        if (kind == TenancyCallerKind.SystemInTenant)
        {
            return catalogue.LiveKeys.AsQueryable();
        }

        // The stored rights are written again when a seat or a role changes, not when the catalogue does, so a
        // key retired since keeps its rows until then. It holds nowhere, so the live keys are passed in: a
        // parameter list, which a storage translates as one.
        var tenant = caller.Tenant!.Value;
        var seat = caller.Seat!.Value;
        var moment = now;
        var live = catalogue.LiveKeys.ToArray();
        return (from right in source.SeatRights
                where right.TenantId.Equals(tenant) && right.SeatId.Equals(seat)
                      && right.StartsAt <= moment && (right.EndsAt == null || right.EndsAt > moment)
                      && live.Contains(right.Key)
                join path in source.UnitPaths on right.UnitId equals path.AncestorId
                where path.TenantId.Equals(tenant) && path.DescendantId.Equals(unit)
                select right.Key).Distinct();
    }

    public IQueryable<TSeatId> SeatsHoldingAt(string key, TUnitId unit)
    {
        var kind = Kind();
        if (!Askable(key) || kind == TenancyCallerKind.Nobody)
        {
            return source.Seats.Where(_ => false).Select(seat => seat.Id);
        }

        var tenant = caller.Tenant!.Value;
        var moment = now;
        var rights = source.SeatRights.Where(right => right.TenantId.Equals(tenant) && right.Key == key
                                                      && right.StartsAt <= moment && (right.EndsAt == null || right.EndsAt > moment));

        if (kind == TenancyCallerKind.Seat)
        {
            // A source that shows a seat only its own rights answers for the other seats itself, by the same rule.
            if (source.SeatsHoldingAt(key, unit) is { } answered)
            {
                return answered;
            }

            // A seat learns about another seat's right where it may read the grant that gives it: at a unit where
            // it manages grants, seats or units, held there or above it, or anywhere once it manages roles for the
            // whole tenant. A key reads only where it applies, so a seat that manages one part of the tree learns
            // nothing of the grants above it or beside it. It always learns about itself.
            var me = caller.Seat!.Value;
            var mine = source.SeatRights.Where(right => right.TenantId.Equals(tenant) && right.SeatId.Equals(me)
                                                        && right.StartsAt <= moment && (right.EndsAt == null || right.EndsAt > moment));
            var managed = from right in mine
                          where right.Key == TenancyKeys.GrantsManage || right.Key == TenancyKeys.SeatsManage || right.Key == TenancyKeys.UnitsManage
                          join path in source.UnitPaths on right.UnitId equals path.AncestorId
                          where path.TenantId.Equals(tenant)
                          select path.DescendantId;
            var forTheTenant = from right in mine
                               where right.Key == TenancyKeys.RolesManage
                               join root in source.Units on right.UnitId equals root.Id
                               where root.TenantId.Equals(tenant) && !root.ParentId.HasValue
                               select right.Key;

            rights = rights.Where(right => right.SeatId.Equals(me) || managed.Contains(right.UnitId) || forTheTenant.Any());
        }

        // The active seats among them whose right reaches the unit: granted there, or at a unit above it.
        return (from right in rights
                join path in source.UnitPaths on right.UnitId equals path.AncestorId
                where path.TenantId.Equals(tenant) && path.DescendantId.Equals(unit)
                join seat in source.Seats on right.SeatId equals seat.Id
                where seat.TenantId.Equals(tenant) && seat.Status == SeatStatus.Active
                select right.SeatId).Distinct();
    }

    public IQueryable<OrganizationUnitRow<TTenantId, TUnitId>> Units()
    {
        if (Kind() == TenancyCallerKind.Nobody)
        {
            return source.Units.Where(_ => false);
        }

        var tenant = caller.Tenant!.Value;
        return source.Units.Where(unit => unit.TenantId.Equals(tenant));
    }

    public IQueryable<RoleRow<TTenantId, TRoleId>> Roles()
    {
        if (Kind() == TenancyCallerKind.Nobody)
        {
            return source.Roles.Where(_ => false);
        }

        var tenant = caller.Tenant!.Value;
        return source.Roles.Where(role => role.TenantId.Equals(tenant));
    }

    public IQueryable<SeatRow<TTenantId, TSeatId>> Seats()
    {
        if (Kind() == TenancyCallerKind.Nobody)
        {
            return source.Seats.Where(_ => false);
        }

        var tenant = caller.Tenant!.Value;
        return source.Seats.Where(seat => seat.TenantId.Equals(tenant));
    }

    public Task<bool> HoldsTenantWideAsync(string key, CancellationToken cancellationToken)
    {
        var kind = Kind();
        if (!Askable(key) || kind == TenancyCallerKind.Nobody)
        {
            return Task.FromResult(false);
        }

        if (kind == TenancyCallerKind.SystemInTenant)
        {
            return Task.FromResult(true);
        }

        var tenant = caller.Tenant!.Value;
        var seat = caller.Seat!.Value;
        var moment = now;
        var atRoot = from right in source.SeatRights
                     where right.TenantId.Equals(tenant) && right.SeatId.Equals(seat) && right.Key == key
                           && right.StartsAt <= moment && (right.EndsAt == null || right.EndsAt > moment)
                     join root in source.Units on right.UnitId equals root.Id
                     where root.TenantId.Equals(tenant) && !root.ParentId.HasValue
                     select right.Key;

        return executor.AnyAsync(atRoot, cancellationToken);
    }

    public Task<bool> HoldsAtAsync(string key, TUnitId unit, CancellationToken cancellationToken)
    {
        var kind = Kind();
        if (!Askable(key) || kind == TenancyCallerKind.Nobody)
        {
            return Task.FromResult(false);
        }

        if (kind == TenancyCallerKind.SystemInTenant)
        {
            // Every key, but only at a unit of its own tenant.
            var tenant = caller.Tenant!.Value;
            return executor.AnyAsync(source.Units.Where(row => row.TenantId.Equals(tenant) && row.Id.Equals(unit)), cancellationToken);
        }

        return executor.AnyAsync(UnitsWhereIHold(key).Where(held => held.Equals(unit)), cancellationToken);
    }

    /// <summary>The caller's kind; system work outside any tenant has nothing to ask about here.</summary>
    private TenancyCallerKind Kind()
        => caller.Kind == TenancyCallerKind.System
            ? throw new InvalidOperationException(
                "System work outside any tenant asks Tenancy nothing: it holds nothing in any tenant. "
                + SystemWorkAdvice.BeginInATenant<TTenantId, TSeatId>("for work inside one"))
            : caller.Kind;

    /// <summary>
    /// Checks that code asks about a key the catalogue knows, and answers whether it can hold anywhere: a
    /// retired key is askable and holds nowhere.
    /// </summary>
    private bool Askable(string key)
    {
        catalogue.RequireAskable(key);
        return catalogue.IsLive(key);
    }

    /// <summary>
    /// Checks that code asks about keys the catalogue knows, every one of them, and answers those that can hold
    /// anywhere, each once, in ordinal order: a retired key is askable and holds nowhere.
    /// </summary>
    private string[] LiveAmong(IReadOnlyCollection<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        foreach (var key in keys)
        {
            catalogue.RequireAskable(key);
        }

        return [.. keys.Where(catalogue.IsLive).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The answers for each of <paramref name="keys"/>, of which there is at least one, as one query: the parts
    /// one after the other, which a storage sends as one statement. No pair comes twice, since each part is for
    /// another key.
    /// </summary>
    private static IQueryable<T> ForEach<T>(string[] keys, Func<string, IQueryable<T>> forKey)
    {
        var all = forKey(keys[0]);
        for (var index = 1; index < keys.Length; index++)
        {
            all = all.Concat(forKey(keys[index]));
        }

        return all;
    }

    private IQueryable<TUnitId> NoUnits() => source.Units.Where(_ => false).Select(unit => unit.Id);
}
