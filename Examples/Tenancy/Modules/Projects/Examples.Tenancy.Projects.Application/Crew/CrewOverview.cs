namespace Examples.Tenancy.Projects.Application.Crew;

/// <summary>A crew member: the seat, the period it is on the crew for, and its crew roles, each by its id.</summary>
/// <remarks>
/// Who is on a crew is for whoever sees the project. Which roles each member holds there, and until when, is for
/// whoever manages that crew: a caller who holds <see cref="RolesKey"/> on the project. The queries that answer
/// a crew hold that themselves, in the statement that reads it, so every way a crew is answered keeps to it: a
/// route, a GraphQL field, and whatever asks next. The caller's own roles on a crew are the caller's to read
/// either way, and are answered beside the crew, by their ids.
/// </remarks>
/// <param name="ProjectId">The project whose crew it is on: what a rule about a member is asked on.</param>
/// <param name="SeatId">The seat.</param>
/// <param name="IsOwner">Whether the seat owns the project.</param>
/// <param name="StartsAt">When the membership starts to count.</param>
/// <param name="EndsAt">When it stops, or <see langword="null"/>.</param>
/// <param name="AppliesNow">Whether it counts now.</param>
/// <param name="Roles">
/// The roles it holds on the crew, each with its own period: by when each starts, then by role.
/// <see langword="null"/> for a caller who does not hold <see cref="RolesKey"/> on the project: not an empty
/// list, which would say the member holds none.
/// </param>
public sealed record CrewOverview(
    ProjectId ProjectId,
    SeatId SeatId,
    bool IsOwner,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    bool AppliesNow,
    IReadOnlyList<CrewRoleOverview>? Roles)
{
    /// <summary>The key a caller holds on the project to be answered <see cref="Roles"/>: the one that manages its crew.</summary>
    public const string RolesKey = ProjectKeys.ManageCrew;
}
