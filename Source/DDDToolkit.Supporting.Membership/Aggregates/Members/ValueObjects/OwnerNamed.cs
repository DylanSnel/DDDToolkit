using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// What naming an owner did to a member list, for the aggregate to act on: it keeps <see cref="Owner"/> as its
/// owner, and raises its own events for what happened, in this order: the member added, or what changed about the
/// membership it had and the roles it held, what the owner before became, the owner changed.
/// <para>
/// An owner's place has no end and holds no role, and naming an owner makes it so: a membership that was to end no
/// longer does, also after the resource is handed on again, and the roles the new owner held go, since it holds by
/// owning. Somebody who was on a resource for a week and owned it for a day is on it for good afterwards.
/// <see cref="EndLifted"/> says the end that went, so an aggregate that wants the former owner off again at that
/// moment can keep it and take the member off the list then.
/// </para>
/// </summary>
/// <param name="Previous">The owner before.</param>
/// <param name="Owner">The owner now: what the aggregate keeps in its owner property.</param>
/// <param name="Added">Whether the new owner was put on the member list now, not being a member before.</param>
/// <param name="BeganAnew">Whether the new owner's membership had ended, and began again now.</param>
/// <param name="RolesDropped">The roles the new owner held and no longer holds, as they were held; empty when it held none.</param>
/// <param name="EndLifted">The end the new owner's membership had and no longer has, or <see langword="null"/> when it had none that was still to come.</param>
/// <param name="StartMoved">The later moment the new owner's membership was to start at, moved to now, or <see langword="null"/>.</param>
/// <param name="PreviousRole">
/// The role the owner before holds from now on, as a member, or <see langword="null"/> when it left the list
/// (<paramref name="PreviousLeft"/>).
/// </param>
/// <param name="PreviousLeft">Whether the owner before was taken off the list.</param>
/// <typeparam name="TMemberId">What a member is known by.</typeparam>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
public sealed record OwnerNamed<TMemberId, TRoleId>(
    TMemberId Previous,
    TMemberId Owner,
    bool Added,
    bool BeganAnew,
    IReadOnlyList<MemberRole<TMemberId, TRoleId>> RolesDropped,
    DateTimeOffset? EndLifted,
    DateTimeOffset? StartMoved,
    TRoleId? PreviousRole,
    bool PreviousLeft)
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>;
