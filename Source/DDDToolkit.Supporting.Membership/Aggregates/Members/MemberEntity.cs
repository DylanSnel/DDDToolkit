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
    /// Keeps the member on the resource from <paramref name="now"/> at the latest and with no end, in
    /// <paramref name="ownerRole"/> with no end either: an owner's place does not run out. A membership that had
    /// ended starts anew at <paramref name="now"/>, without the roles it held: they ran out with it, and naming
    /// an owner gives the owner's role and no other.
    /// <para>
    /// A membership that was still to start, or still to end, loses that start or that end, and the roles
    /// held in it do not gain by that: a role counted only inside its membership, so each is kept to the
    /// period the membership had. One that was to count from the membership's start counts from that moment
    /// still, and no sooner; one with no end of its own, or one that ran past the membership's end, ends
    /// where the membership would have; and one whose own period was outside that one altogether, so it never
    /// counted, goes. Otherwise somebody on a resource for a week would hold its roles for good by owning it
    /// for a day, and somebody who was to come next month would hold them from today.
    /// </para>
    /// </summary>
    /// <returns>What happened to the membership, and whether the role was given now.</returns>
    internal OwnersPlace StayAsOwner(TRoleId ownerRole, DateTimeOffset now)
    {
        var beganAnew = false;
        List<MemberRole<TMemberId, TRoleId>> dropped = [];
        DateTimeOffset? lifted = null;
        DateTimeOffset? moved = null;

        if (EndedBy(now))
        {
            beganAnew = true;
            dropped.AddRange(_roles);
            _roles.Clear();
            StartsAt = now;
        }
        else
        {
            if (StartsAt > now)
            {
                moved = StartsAt;
                StartsAt = now;
            }

            lifted = EndsAt;
            if (moved is not null || lifted is not null)
            {
                foreach (var held in _roles.Where(held => !held.RoleId.Equals(ownerRole)).ToList())
                {
                    // What the role counted for: its own period, inside the one the membership had.
                    var from = moved is { } start && held.StartsAt < start ? start : held.StartsAt;
                    var until = lifted is { } end && (held.EndsAt is null || held.EndsAt > end) ? end : held.EndsAt;
                    if (until is { } last && last <= from)
                    {
                        // It was outside the membership's period, before it or after it, so it never counted.
                        _roles.Remove(held);
                        dropped.Add(held);
                    }
                    else
                    {
                        held.KeepTo(from, until);
                    }
                }
            }
        }

        EndsAt = null;

        var given = Find(ownerRole) is not { EndsAt: null } owners || owners.StartsAt > now;
        if (given)
        {
            Give(ownerRole, MemberPeriod.Open(now), givenBy: null);
        }

        return new OwnersPlace(given, beganAnew, dropped, lifted, moved);
    }

    /// <summary>What keeping a member on a resource as its owner did to the membership it had.</summary>
    /// <param name="RoleGiven">Whether the owner's role was given now; <see langword="false"/> when the member held it already, with no end.</param>
    /// <param name="BeganAnew">Whether the membership had ended, and began again now.</param>
    /// <param name="RolesDropped">The roles the member held and no longer holds.</param>
    /// <param name="EndLifted">The end the membership had and no longer has, or <see langword="null"/>.</param>
    /// <param name="StartMoved">The later start the membership had, moved to now, or <see langword="null"/>.</param>
    internal readonly record struct OwnersPlace(
        bool RoleGiven,
        bool BeganAnew,
        IReadOnlyList<MemberRole<TMemberId, TRoleId>> RolesDropped,
        DateTimeOffset? EndLifted,
        DateTimeOffset? StartMoved);
}
