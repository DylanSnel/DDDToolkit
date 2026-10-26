namespace Examples.Tenancy.Projects.Application.Crew;

/// <summary>
/// What turns the crew a reading returned into what a caller is shown: the mapping to <see cref="CrewOverview"/>,
/// in the one order every answer lists a crew in. It reads nothing, so it costs no statement.
/// </summary>
internal static class CrewOverviews
{
    /// <summary>
    /// The crew of a project: the owner first, then by when each membership starts, then by seat, and each
    /// member's roles by when each starts, then by role. The roles are there only for a caller who manages the
    /// crew, which the reading answered in the statement that read it
    /// (<see cref="ProjectCrewData.CallerManagesCrew"/>); for anybody else each member is answered without them.
    /// </summary>
    /// <param name="project">The project's crew, as the reading returned it.</param>
    /// <param name="now">The moment a membership, and a role held in it, must apply at to count now.</param>
    public static IReadOnlyList<CrewOverview> From(ProjectCrewData project, DateTimeOffset now)
    {
        // An order of the module's own facts, the same every time: the module has no names to order by, and a
        // screen that shows names orders by them itself.
        return [.. project.Crew
            .OrderByDescending(member => member.SeatId == project.OwnerSeatId)
            .ThenBy(member => member.StartsAt)
            .ThenBy(member => member.SeatId.Value)
            .Select(member =>
            {
                var applies = AppliesAt(member.StartsAt, member.EndsAt, now);
                return new CrewOverview(
                    project.Project,
                    member.SeatId,
                    member.SeatId == project.OwnerSeatId,
                    member.StartsAt,
                    member.EndsAt,
                    applies,
                    project.CallerManagesCrew
                        ? [.. member.Roles
                            .OrderBy(held => held.StartsAt)
                            .ThenBy(held => held.RoleId.Value)
                            .Select(held => new CrewRoleOverview(held.RoleId, held.StartsAt, held.EndsAt, applies && AppliesAt(held.StartsAt, held.EndsAt, now)))]
                        : null);
            })];
    }

    /// <summary>
    /// The roles <paramref name="seat"/> holds on the crew now, by their ids, ordered by the id's value: each role
    /// whose own period applies, in a membership that applies. What a caller reads of its own place, whether or
    /// not it manages the crew.
    /// </summary>
    /// <param name="project">The project's crew, as the reading returned it.</param>
    /// <param name="seat">The seat asked about: the caller's own.</param>
    /// <param name="now">The moment a membership, and a role held in it, must apply at.</param>
    public static IReadOnlyList<ProjectRoleId> RolesHeldNow(ProjectCrewData project, SeatId seat, DateTimeOffset now)
        => [.. project.Crew
            .Where(member => member.SeatId == seat && AppliesAt(member.StartsAt, member.EndsAt, now))
            .SelectMany(member => member.Roles)
            .Where(held => AppliesAt(held.StartsAt, held.EndsAt, now))
            .Select(held => held.RoleId)
            .OrderBy(role => role.Value)];

    /// <summary>Whether a period counts at <paramref name="moment"/>: it has started, and has not ended.</summary>
    private static bool AppliesAt(DateTimeOffset startsAt, DateTimeOffset? endsAt, DateTimeOffset moment)
        => startsAt <= moment && (endsAt is null || endsAt > moment);
}
