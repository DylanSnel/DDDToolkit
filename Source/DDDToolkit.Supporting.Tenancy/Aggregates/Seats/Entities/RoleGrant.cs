using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// A role a seat holds at the unit of one of its placements, for a period. It is known by its role within
/// its placement: a placement holds a role at most once.
/// <para>
/// A plain class rather than a toolkit entity, and not extended by the application: a grant is a fact
/// about a seat, and only the seat changes it.
/// </para>
/// </summary>
/// <typeparam name="TSeatId">The application's seat id.</typeparam>
/// <typeparam name="TRoleId">The application's role id.</typeparam>
public sealed class RoleGrant<TSeatId, TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>For Entity Framework.</summary>
    private RoleGrant()
    {
    }

    internal RoleGrant(TRoleId roleId, GrantPeriod period, TSeatId? grantedBy, string? reason)
    {
        RoleId = roleId;
        StartsAt = period.Starts;
        EndsAt = period.Ends;
        GrantedBy = grantedBy;
        Reason = reason;
    }

    /// <summary>The role granted.</summary>
    public TRoleId RoleId { get; private set; }

    /// <summary>The first moment the grant applies.</summary>
    public DateTimeOffset StartsAt { get; private set; }

    /// <summary>The first moment it no longer applies, or <see langword="null"/> when it has no end.</summary>
    public DateTimeOffset? EndsAt { get; private set; }

    /// <summary>The seat that made the grant, or <see langword="null"/> when system work made it with no seat acting.</summary>
    public TSeatId? GrantedBy { get; private set; }

    /// <summary>Why it was granted, at most 500 characters, or <see langword="null"/>.</summary>
    public string? Reason { get; private set; }

    /// <summary>Whether the grant applies at <paramref name="moment"/>: it has started, and has not ended.</summary>
    /// <param name="moment">The moment to ask about, usually now.</param>
    public bool AppliesAt(DateTimeOffset moment) => StartsAt <= moment && (EndsAt is null || EndsAt > moment);
}
