using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// One member of a resource, for a period, with the roles it holds there. The application declares its own
/// class with <see cref="MemberAttribute{TId, TMemberId, TRoleId, TResource}"/>, as a child entity of the aggregate the
/// members belong to: loaded and saved with it, and changed only through it.
/// <para>
/// Being a member and holding powers are two things, each with its own dates. The membership says the member
/// is on the resource from <see cref="StartsAt"/> until <see cref="EndsAt"/>. What it may do there comes from
/// its <see cref="Roles"/>, each held for a period of its own, so a role can end while the membership goes on,
/// and a member can hold two roles at once. A role counts only while the membership does
/// (<see cref="HoldsAt"/>). A member with no role is a member without any rights from it but what the
/// resource's rules give membership alone.
/// </para>
/// <para>
/// A member belongs to the member list of its resource, and nothing else changes it: every mutator here is
/// internal, and <see cref="MemberList{TMember, TId, TMemberId, TRoleId}"/> checks the rules about the whole
/// list before it calls one. The one rule a member can tell by itself, that it holds a role once, is nested
/// here and runs for the application's class whenever its aggregate is checked.
/// </para>
/// </summary>
/// <typeparam name="TId">The member class's own id.</typeparam>
/// <typeparam name="TMemberId">What a member is known by.</typeparam>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
[EntityBase]
public abstract partial class MemberEntity<TId, TMemberId, TRoleId>
    where TId : struct, IEntityId, IEquatable<TId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    // Written by hand rather than as a generated collection: a held role is not a toolkit entity.
    private readonly List<MemberRole<TMemberId, TRoleId>> _roles = [];

    /// <summary>Who the member is.</summary>
    public TMemberId MemberId { get; private set; }

    /// <summary>The first moment the membership counts.</summary>
    public DateTimeOffset StartsAt { get; private set; }

    /// <summary>The first moment it no longer counts, or <see langword="null"/> when it has no end.</summary>
    public DateTimeOffset? EndsAt { get; private set; }

    /// <summary>
    /// The member that added this one, or <see langword="null"/> for system work and for an owner the resource
    /// was opened with or that was named owner without being a member.
    /// </summary>
    public TMemberId? AddedBy { get; private set; }

    /// <summary>The roles the member holds on the resource, at most one per role. A read-only view: only the member list changes them.</summary>
    public IReadOnlyList<MemberRole<TMemberId, TRoleId>> Roles => _roles.AsReadOnly();

    /// <summary>Whether the membership counts at <paramref name="moment"/>: it has started, and has not ended.</summary>
    /// <param name="moment">The moment to ask about, usually now.</param>
    public bool AppliesAt(DateTimeOffset moment) => StartsAt <= moment && (EndsAt is null || EndsAt > moment);

    /// <summary>
    /// Whether the member holds <paramref name="role"/> at <paramref name="moment"/>: the role's own period
    /// applies then, and so does the membership it is held in. A role with no end of its own stops counting
    /// when the membership ends.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <param name="moment">The moment to ask about, usually now.</param>
    public bool HoldsAt(TRoleId role, DateTimeOffset moment)
        => AppliesAt(moment) && _roles.Any(held => held.RoleId.Equals(role) && held.AppliesAt(moment));

    /// <summary>
    /// Whether the membership has ended by <paramref name="moment"/>: it had an end, and that end is not after
    /// the moment. An ended membership is over for good: the member comes back with a new one.
    /// </summary>
    /// <param name="moment">The moment to ask about.</param>
    public bool EndedBy(DateTimeOffset moment) => EndsAt is { } ended && ended <= moment;

    /// <summary>
    /// What a constructor would do: gives a new instance its id, its member, its period and who added it, with
    /// no role yet. Called once, by the member list, right after the instance is made.
    /// </summary>
    internal void InitializeNew(TId id, TMemberId member, MemberPeriod period, TMemberId? addedBy)
    {
        Id = id;
        MemberId = member;
        StartsAt = period.Starts;
        EndsAt = period.Ends;
        AddedBy = addedBy;
    }

    /// <summary>The member's hold of <paramref name="role"/>, ended or not, or <see langword="null"/>.</summary>
    internal MemberRole<TMemberId, TRoleId>? Find(TRoleId role) => _roles.FirstOrDefault(held => held.RoleId.Equals(role));

    /// <summary>
    /// Gives the member <paramref name="role"/> for <paramref name="period"/>, in the place of whatever hold
    /// of that role it has: a member holds a role once. The member list has seen to it that a hold which
    /// still runs, or is still to start, is not replaced.
    /// </summary>
    internal MemberRole<TMemberId, TRoleId> Give(TRoleId role, MemberPeriod period, TMemberId? givenBy)
    {
        _roles.RemoveAll(held => held.RoleId.Equals(role));
        var given = new MemberRole<TMemberId, TRoleId>(role, period, givenBy);
        _roles.Add(given);
        return given;
    }

    /// <summary>Takes <paramref name="role"/> from the member, and answers what was held; <see langword="null"/> when it does not hold it.</summary>
    internal MemberRole<TMemberId, TRoleId>? Take(TRoleId role)
    {
        var held = Find(role);
        if (held is not null)
        {
            _roles.Remove(held);
        }

        return held;
    }

    /// <summary>
    /// Keeps the member on the resource from <paramref name="now"/> at the latest and with no end, holding no role:
    /// an owner's place does not run out, and the owner holds what it holds by owning the resource, never through a
    /// role. A membership that had ended starts anew at <paramref name="now"/>; one that was still to start, or
    /// still to end, loses that start or that end. Either way the roles the member held go: they gave what the owner
    /// now holds by owning, or more than an owner holds.
    /// </summary>
    /// <param name="now">When the member becomes the owner.</param>
    /// <returns>What happened to the membership, and the roles that went.</returns>
    internal OwnersPlace StayAsOwner(DateTimeOffset now)
    {
        var beganAnew = EndedBy(now);
        DateTimeOffset? lifted = beganAnew ? null : EndsAt;
        DateTimeOffset? moved = !beganAnew && StartsAt > now ? StartsAt : null;
        if (beganAnew || moved is not null)
        {
            StartsAt = now;
        }

        EndsAt = null;
        List<MemberRole<TMemberId, TRoleId>> dropped = [.. _roles];
        _roles.Clear();

        return new OwnersPlace(beganAnew, dropped, lifted, moved);
    }

    internal readonly record struct OwnersPlace(
        bool BeganAnew,
        IReadOnlyList<MemberRole<TMemberId, TRoleId>> RolesDropped,
        DateTimeOffset? EndLifted,
        DateTimeOffset? StartMoved);
}
