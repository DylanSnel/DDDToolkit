namespace Examples.Tenancy.Projects.Application.Crew;

/// <summary>The crew of one project as a reading returns it: plain data, with the owner the crew is ordered by.</summary>
/// <param name="Project">The project.</param>
/// <param name="OwnerSeatId">The seat that owns it.</param>
/// <param name="CurrentSeatManagesCrew">
/// Whether the caller holds the permission that manages this crew, on this project, read in the statement that read the
/// crew: what decides whether the members' roles are answered.
/// </param>
/// <param name="Crew">Its crew, in no particular order.</param>
public sealed record ProjectCrewData(ProjectId Project, SeatId OwnerSeatId, bool CurrentSeatManagesCrew, IReadOnlyList<CrewMemberData> Crew);
