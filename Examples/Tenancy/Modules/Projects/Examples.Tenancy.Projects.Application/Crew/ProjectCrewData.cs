namespace Examples.Tenancy.Projects.Application.Crew;

/// <summary>The crew of one project as a reading returns it: plain data, with the owner the crew is ordered by.</summary>
/// <param name="Project">The project.</param>
/// <param name="OwnerSeatId">The seat that owns it.</param>
/// <param name="CallerManagesCrew">
/// Whether the caller holds the key that manages this crew, on this project, read in the statement that read the
/// crew: what decides whether the members' roles are answered.
/// </param>
/// <param name="Crew">Its crew, in no particular order.</param>
public sealed record ProjectCrewData(ProjectId Project, SeatId OwnerSeatId, bool CallerManagesCrew, IReadOnlyList<CrewMemberData> Crew);
