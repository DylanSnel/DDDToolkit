using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.UseCases;

/// <summary>A role a member holds, by its id, with the period it holds it for.</summary>
/// <param name="Role">The role.</param>
/// <param name="StartsAt">When the role starts to count.</param>
/// <param name="EndsAt">When it stops, or <see langword="null"/>.</param>
/// <param name="AppliesNow">Whether it counts now: its own period applies, and so does the membership it is held in.</param>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
public sealed record MemberRoleOverview<TRoleId>(TRoleId Role, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, bool AppliesNow)
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>;
