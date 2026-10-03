using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// What naming an owner did to a member list, for the aggregate to act on: it keeps <see cref="Owner"/> as its
/// owner, and raises its own events for what happened, in this order: the member added, or what changed about
/// the membership it had, the owner's role given, the owner's role taken from the one before, the owner
/// changed.
/// <para>
/// An owner's place has no end, and naming an owner makes it so: a membership that was to end no longer
/// does, also after the resource is handed on again. Somebody who was on a resource for a week and owned it
/// for a day is on it for good afterwards. <see cref="EndLifted"/> says the end that went, so an aggregate
/// that wants the former owner off again at that moment can keep it and take the member off the list then.
/// </para>
/// </summary>
/// <param name="Previous">The owner before.</param>
/// <param name="Owner">The owner now: what the aggregate keeps in its owner property.</param>
/// <param name="Added">Whether the new owner was put on the member list now, not being a member before.</param>
/// <param name="RoleGiven">Whether the new owner was given the owner's role now; <see langword="false"/> when it held that role already, with no end.</param>
/// <param name="RoleTaken">Whether the owner before lost the owner's role now; <see langword="false"/> when it did not hold it.</param>
/// <param name="BeganAnew">
/// Whether the new owner's membership had ended, and began again now: a new membership from this moment on,
/// without the roles of the one that ended (<paramref name="RolesDropped"/>).
/// </param>
/// <param name="RolesDropped">
/// The roles the new owner held and no longer holds, as they were held: every role of a membership that had
/// ended (<paramref name="BeganAnew"/>), and a role that never counted, since its period was outside the
/// one the membership had: one that was to start only when the membership would have been over
/// (<paramref name="EndLifted"/>), or that had ended before the membership was to start
/// (<paramref name="StartMoved"/>). Empty when nothing went.
/// </param>
/// <param name="EndLifted">
/// The end the new owner's membership had and no longer has, or <see langword="null"/> when it had none that
/// was still to come. The roles held in that membership stop there all the same: a role with no end of its
/// own, or one that ran past it, now ends at this moment, since a role counted only inside its membership
/// and would otherwise count for longer than it ever did. The owner's role is not among them.
/// </param>
/// <param name="StartMoved">
/// The later moment the new owner's membership was to start at, moved to now, or <see langword="null"/> when
/// it had started already or begins anew. The roles held in that membership start no sooner for it: a role
/// that was to count from the membership's start, whatever earlier start of its own it had, still counts
/// from this moment, for the same reason an end that is lifted stays the end of the roles. The owner's role
/// is not among them.
/// </param>
/// <typeparam name="TMemberId">What a member is known by.</typeparam>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
public sealed record OwnerNamed<TMemberId, TRoleId>(
    TMemberId Previous,
    TMemberId Owner,
    bool Added,
    bool RoleGiven,
    bool RoleTaken,
    bool BeganAnew,
    IReadOnlyList<MemberRole<TMemberId, TRoleId>> RolesDropped,
    DateTimeOffset? EndLifted,
    DateTimeOffset? StartMoved)
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>;
