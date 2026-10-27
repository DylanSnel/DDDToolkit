using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// The access queries over one read source, for one caller at one moment.
/// <para>
/// Every query is plain LINQ over the source's sets, so a storage can translate it: ids are compared with
/// <c>Equals</c>, an empty answer is the source's own set filtered to nothing, and every set that has a
/// tenant is kept to the caller's tenant here too, next to whatever filter the storage applies.
/// </para>
/// </summary>
internal sealed class TenancyQueries<TTenantId, TSeatId, TUnitId, TRoleId>(
    ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId> source,
    IQueryExecutor executor,
    TenancyCatalogue catalogue,
    TenancyCaller<TTenantId, TSeatId> caller,
    DateTimeOffset now)
    : ITenancyQueries<TTenantId, TSeatId, TUnitId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    public IQueryable<TUnitId> UnitsWhereIHold(string permission)
    {
        var kind = Kind();
        if (!Askable(permission) || kind == TenancyCallerKind.Nobody)
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
                where right.TenantId.Equals(tenant) && right.SeatId.Equals(seat) && right.Permission == permission
                      && right.StartsAt <= moment && (right.EndsAt == null || right.EndsAt > moment)
                join path in source.UnitPaths on right.UnitId equals path.AncestorId
                where path.TenantId.Equals(tenant)
                select path.DescendantId).Distinct();
    }

    public IQueryable<UnitPermission<TUnitId>> UnitsWhereIHold(IReadOnlyCollection<string> permissions)
    {
        var kind = Kind();
        var asked = LiveAmong(permissions);
        if (asked.Length == 0 || kind == TenancyCallerKind.Nobody)
        {
            return source.Units.Where(_ => false).Select(unit => new UnitPermission<TUnitId> { Unit = unit.Id, Permission = string.Empty });
        }

        // A pair is made by setting its members, which a storage reads back as columns: a query composed over
        // the pairs, one that keeps a unit's or picks out the permissions, stays one statement.
        var tenant = caller.Tenant!.Value;
        if (kind == TenancyCallerKind.SystemInTenant)
        {
            // Every permission, at every unit of its tenant.
            return ForEach(asked, permission => source.Units
                .Where(unit => unit.TenantId.Equals(tenant))
                .Select(unit => new UnitPermission<TUnitId> { Unit = unit.Id, Permission = permission }));
        }

        var seat = caller.Seat!.Value;
        var moment = now;
        return (from right in source.SeatRights
                where right.TenantId.Equals(tenant) && right.SeatId.Equals(seat) && asked.Contains(right.Permission)
                      && right.StartsAt <= moment && (right.EndsAt == null || right.EndsAt > moment)
                join path in source.UnitPaths on right.UnitId equals path.AncestorId
                where path.TenantId.Equals(tenant)
                select new UnitPermission<TUnitId> { Unit = path.DescendantId, Permission = right.Permission }).Distinct();
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

    public IQueryable<TRoleId> RolesWithPermission(string permission)
    {
        var kind = Kind();
        if (!Askable(permission) || kind == TenancyCallerKind.Nobody)
        {
            return source.Roles.Where(_ => false).Select(role => role.Id);
        }

        var tenant = caller.Tenant!.Value;
        return source.Roles
            .Where(role => role.TenantId.Equals(tenant) && role.Status == RoleStatus.Active && role.Permissions.Contains(permission))
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

    public IQueryable<RoleWithPermission<TRoleId>> RolesWithPermission(IReadOnlyCollection<string> permissions)
    {
        var kind = Kind();
        var asked = LiveAmong(permissions);
        if (asked.Length == 0 || kind == TenancyCallerKind.Nobody)
        {
            return source.Roles.Where(_ => false).Select(role => new RoleWithPermission<TRoleId> { Role = role.Id, Permission = string.Empty });
        }

        // The roles that grant a permission, for each permission: a role's permissions are one column, which not every storage can
        // open into rows, while every one can ask whether a permission is among them.
        var tenant = caller.Tenant!.Value;
        return ForEach(asked, permission => source.Roles
            .Where(role => role.TenantId.Equals(tenant) && role.Status == RoleStatus.Active && role.Permissions.Contains(permission))
            .Select(role => new RoleWithPermission<TRoleId> { Role = role.Id, Permission = permission }));
    }

    public IQueryable<string> PermissionsIHoldAt(TUnitId unit)
    {
        var kind = Kind();
        if (kind == TenancyCallerKind.Nobody)
        {
            return source.SeatRights.Where(_ => false).Select(right => right.Permission);
        }

        if (kind == TenancyCallerKind.SystemInTenant)
        {
            return catalogue.LivePermissions.AsQueryable();
        }

        // The stored rights are written again when a seat or a role changes, not when the catalogue does, so a
        // permission retired since keeps its rows until then. It holds nowhere, so the live permissions are passed in: a
        // parameter list, which a storage translates as one.
        var tenant = caller.Tenant!.Value;
        var seat = caller.Seat!.Value;
        var moment = now;
        var live = catalogue.LivePermissions.ToArray();
        return (from right in source.SeatRights
                where right.TenantId.Equals(tenant) && right.SeatId.Equals(seat)
                      && right.StartsAt <= moment && (right.EndsAt == null || right.EndsAt > moment)
                      && live.Contains(right.Permission)
                join path in source.UnitPaths on right.UnitId equals path.AncestorId
                where path.TenantId.Equals(tenant) && path.DescendantId.Equals(unit)
                select right.Permission).Distinct();
    }

    public IQueryable<TSeatId> SeatsHoldingPermissionAt(string permission, TUnitId unit)
    {
        var kind = Kind();
        if (!Askable(permission) || kind == TenancyCallerKind.Nobody)
        {
            return source.Seats.Where(_ => false).Select(seat => seat.Id);
        }

        var tenant = caller.Tenant!.Value;
        var moment = now;
        var rights = source.SeatRights.Where(right => right.TenantId.Equals(tenant) && right.Permission == permission
                                                      && right.StartsAt <= moment && (right.EndsAt == null || right.EndsAt > moment));

        if (kind == TenancyCallerKind.Seat)
        {
            // A source that shows a seat only its own rights answers for the other seats itself, by the same rule.
            if (source.SeatsHoldingPermissionAt(permission, unit) is { } answered)
            {
                return answered;
            }

            // A seat learns about another seat's right where it may read the grant that gives it: at a unit where
            // it manages grants, seats or units, held there or above it, or anywhere once it manages roles for the
            // whole tenant. A permission reads only where it applies, so a seat that manages one part of the tree learns
            // nothing of the grants above it or beside it. It always learns about itself.
            var me = caller.Seat!.Value;
            var mine = source.SeatRights.Where(right => right.TenantId.Equals(tenant) && right.SeatId.Equals(me)
                                                        && right.StartsAt <= moment && (right.EndsAt == null || right.EndsAt > moment));
            var managed = from right in mine
                          where right.Permission == TenancyPermissions.GrantsManage || right.Permission == TenancyPermissions.SeatsManage || right.Permission == TenancyPermissions.UnitsManage
                          join path in source.UnitPaths on right.UnitId equals path.AncestorId
                          where path.TenantId.Equals(tenant)
                          select path.DescendantId;
            var forTheTenant = from right in mine
                               where right.Permission == TenancyPermissions.RolesManage
                               join root in source.Units on right.UnitId equals root.Id
                               where root.TenantId.Equals(tenant) && !root.ParentId.HasValue
                               select right.Permission;

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

    public Task<bool> HoldsAtTenantAsync(string permission, CancellationToken cancellationToken)
    {
        var kind = Kind();
        if (!Askable(permission) || kind == TenancyCallerKind.Nobody)
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
                     where right.TenantId.Equals(tenant) && right.SeatId.Equals(seat) && right.Permission == permission
                           && right.StartsAt <= moment && (right.EndsAt == null || right.EndsAt > moment)
                     join root in source.Units on right.UnitId equals root.Id
                     where root.TenantId.Equals(tenant) && !root.ParentId.HasValue
                     select right.Permission;

        return executor.AnyAsync(atRoot, cancellationToken);
    }

    public Task<bool> HoldsAtAsync(string permission, TUnitId unit, CancellationToken cancellationToken)
    {
        var kind = Kind();
        if (!Askable(permission) || kind == TenancyCallerKind.Nobody)
        {
            return Task.FromResult(false);
        }

        if (kind == TenancyCallerKind.SystemInTenant)
        {
            // Every permission, but only at a unit of its own tenant.
            var tenant = caller.Tenant!.Value;
            return executor.AnyAsync(source.Units.Where(row => row.TenantId.Equals(tenant) && row.Id.Equals(unit)), cancellationToken);
        }

        return executor.AnyAsync(UnitsWhereIHold(permission).Where(held => held.Equals(unit)), cancellationToken);
    }

    /// <summary>The caller's kind; system work outside any tenant has nothing to ask about here.</summary>
    private TenancyCallerKind Kind()
        => caller.Kind == TenancyCallerKind.System
            ? throw new InvalidOperationException(
                "System work outside any tenant asks Tenancy nothing: it holds nothing in any tenant. "
                + SystemWorkAdvice.BeginInATenant("for work inside one"))
            : caller.Kind;

    /// <summary>
    /// Checks that code asks about a permission the catalogue knows, and answers whether it can hold anywhere: a
    /// retired permission is askable and holds nowhere.
    /// </summary>
    private bool Askable(string permission)
    {
        catalogue.RequireAskable(permission);
        return catalogue.IsLive(permission);
    }

    /// <summary>
    /// Checks that code asks about permissions the catalogue knows, every one of them, and answers those that can hold
    /// anywhere, each once, in ordinal order: a retired permission is askable and holds nowhere.
    /// </summary>
    private string[] LiveAmong(IReadOnlyCollection<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        foreach (var permission in permissions)
        {
            catalogue.RequireAskable(permission);
        }

        return [.. permissions.Where(catalogue.IsLive).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The answers for each of <paramref name="permissions"/>, of which there is at least one, as one query: the parts
    /// one after the other, which a storage sends as one statement. No pair comes twice, since each part is for
    /// another permission.
    /// </summary>
    private static IQueryable<T> ForEach<T>(string[] permissions, Func<string, IQueryable<T>> forPermission)
    {
        var all = forPermission(permissions[0]);
        for (var index = 1; index < permissions.Length; index++)
        {
            all = all.Concat(forPermission(permissions[index]));
        }

        return all;
    }

    private IQueryable<TUnitId> NoUnits() => source.Units.Where(_ => false).Select(unit => unit.Id);
}
