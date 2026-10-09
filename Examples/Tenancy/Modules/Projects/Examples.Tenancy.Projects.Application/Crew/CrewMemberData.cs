namespace Examples.Tenancy.Projects.Application.Crew;

/// <summary>A crew member as a reading returns it.</summary>
/// <param name="SeatId">The seat.</param>
/// <param name="StartsAt">The first moment the membership counts.</param>
/// <param name="EndsAt">The first moment it no longer counts, or <see langword="null"/> when it has no end.</param>
/// <param name="Roles">The roles it holds on the crew, in no particular order.</param>
public sealed record CrewMemberData(SeatId SeatId, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, IReadOnlyList<CrewRoleData> Roles);
