using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Where a seat belongs: one unit, with the roles granted to the seat at that unit. Within its seat it is
/// known by that unit, because a seat has at most one placement per unit, and at most one of them is primary.
/// <para>
/// A placement says where a seat belongs; what it may do there comes from the roles granted with it. A plain
/// class rather than a toolkit entity, and not extended by the application: only the seat changes it.
/// </para>
/// </summary>
/// <typeparam name="TSeatId">The application's seat id.</typeparam>
/// <typeparam name="TUnitId">The application's unit id.</typeparam>
/// <typeparam name="TRoleId">The application's role id.</typeparam>
public sealed class Placement<TSeatId, TUnitId, TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    private readonly List<RoleGrant<TSeatId, TRoleId>> _grants = [];

    /// <summary>For Entity Framework.</summary>
    private Placement()
    {
    }

    internal Placement(TUnitId unitId, bool isPrimary, DateTimeOffset placedAt, TSeatId? placedBy)
    {
        UnitId = unitId;
        IsPrimary = isPrimary;
        PlacedAt = placedAt;
        PlacedBy = placedBy;
    }

    /// <summary>The unit the seat is placed in.</summary>
    public TUnitId UnitId { get; private set; }

    /// <summary>Whether this is the seat's primary placement.</summary>
    public bool IsPrimary { get; private set; }

    /// <summary>When the seat was placed.</summary>
    public DateTimeOffset PlacedAt { get; private set; }

    /// <summary>The seat that placed it, or <see langword="null"/> when system work did with no seat acting.</summary>
    public TSeatId? PlacedBy { get; private set; }

    /// <summary>The roles the seat holds at this unit. A read-only view: only the seat changes them.</summary>
    public IReadOnlyList<RoleGrant<TSeatId, TRoleId>> Grants => _grants.AsReadOnly();

    /// <summary>The grant of <paramref name="roleId"/> here, or <see langword="null"/>.</summary>
    internal RoleGrant<TSeatId, TRoleId>? FindGrant(TRoleId roleId)
        => _grants.FirstOrDefault(grant => grant.RoleId.Equals(roleId));

    internal void SetPrimary(bool isPrimary) => IsPrimary = isPrimary;

    internal RoleGrant<TSeatId, TRoleId> AddGrant(TRoleId roleId, GrantPeriod period, TSeatId? grantedBy, string? reason)
    {
        var grant = new RoleGrant<TSeatId, TRoleId>(roleId, period, grantedBy, reason);
        _grants.Add(grant);
        return grant;
    }

    internal bool RemoveGrant(TRoleId roleId) => _grants.RemoveAll(grant => grant.RoleId.Equals(roleId)) > 0;
}
