using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.UseCases;

/// <summary>A member of a resource as a caller is shown it: who, for which period, and the roles it holds, each by its id.</summary>
/// <param name="Member">Who the member is.</param>
/// <param name="IsOwner">Whether the member owns the resource.</param>
/// <param name="StartsAt">When the membership starts to count.</param>
/// <param name="EndsAt">When it stops, or <see langword="null"/>.</param>
/// <param name="AppliesNow">Whether it counts now.</param>
/// <param name="Roles">The roles the member holds, each with its own period: by when each starts, then by role.</param>
/// <typeparam name="TMemberId">What a member is known by.</typeparam>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
public sealed record MemberOverview<TMemberId, TRoleId>(
    TMemberId Member,
    bool IsOwner,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    bool AppliesNow,
    IReadOnlyList<MemberRoleOverview<TRoleId>> Roles)
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>;
