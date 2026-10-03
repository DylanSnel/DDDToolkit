using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// A role a member holds on a resource, for a period of its own. Within its member it is known by its role:
/// a member holds a role at most once.
/// <para>
/// Being a member and holding a role are two things, each with its own dates: a role can end while the
/// membership goes on, and a member can hold two roles at once. A role counts only while its own period
/// applies and the membership it is held in does.
/// </para>
/// <para>
/// A plain class rather than a toolkit entity, and not extended by the application: a held role is a fact
/// about a member, and only the member list changes it. It remembers which role it is, never what the role
/// gives: that is asked where the role is used.
/// </para>
/// </summary>
/// <typeparam name="TMemberId">What a member is known by.</typeparam>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
public sealed class MemberRole<TMemberId, TRoleId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>For Entity Framework.</summary>
    private MemberRole()
    {
    }

    internal MemberRole(TRoleId roleId, MemberPeriod period, TMemberId? givenBy)
    {
        RoleId = roleId;
        StartsAt = period.Starts;
        EndsAt = period.Ends;
        GivenBy = givenBy;
    }

    /// <summary>The role held.</summary>
    public TRoleId RoleId { get; private set; }

    /// <summary>The first moment the role counts.</summary>
    public DateTimeOffset StartsAt { get; private set; }

    /// <summary>The first moment it no longer counts, or <see langword="null"/> when it has no end.</summary>
    public DateTimeOffset? EndsAt { get; private set; }

    /// <summary>The member that gave it, or <see langword="null"/> for system work and for an owner's role.</summary>
    public TMemberId? GivenBy { get; private set; }

    /// <summary>
    /// Whether the role's own period applies at <paramref name="moment"/>: it has started, and has not ended.
    /// Whether the role counts then also takes the membership it is held in
    /// (<see cref="MemberEntity{TId, TMemberId, TRoleId}.HoldsAt"/>).
    /// </summary>
    /// <param name="moment">The moment to ask about, usually now.</param>
    public bool AppliesAt(DateTimeOffset moment) => StartsAt <= moment && (EndsAt is null || EndsAt > moment);

    /// <summary>
    /// Keeps the role to the period from <paramref name="from"/> until <paramref name="until"/>: what holds a
    /// role inside the period its membership had when the membership itself loses its start or its end. The
    /// member says the two moments, each the role's own or the membership's, whichever leaves less.
    /// </summary>
    internal void KeepTo(DateTimeOffset from, DateTimeOffset? until)
    {
        StartsAt = from;
        EndsAt = until;
    }
}
