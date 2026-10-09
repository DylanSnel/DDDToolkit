using Examples.Tenancy.Projects.Application.Overview.Queries;

namespace Examples.Tenancy.Projects.Application.Overview;

/// <summary>
/// A project as the caller sees it: what <see cref="VisibleProjects"/> lists, and what <see cref="ProjectDetail"/>
/// and <see cref="ProjectsById"/> answer with, so it has a file of its own.
/// </summary>
/// <remarks>
/// The project's own row, and how the caller reaches it: nothing that takes another read. Its crew
/// (<see cref="CrewsOfProjects"/>) and what the caller may do to it (<see cref="AbilitiesOnProjects"/>) are asked
/// for all the projects of an answer at once, by whoever shows them, so a list that shows neither reads neither.
/// <para>
/// It names the unit and the owner by their ids, and by nothing else. What a seat or a unit is called is Tenancy's
/// to say, and the rows this module reads of Tenancy carry no name: whoever shows a project asks Tenancy's
/// directory what the ids are called. So the name and the number are the only texts here, and both are the
/// project's own.
/// </para>
/// </remarks>
/// <param name="Id">The project.</param>
/// <param name="Version">
/// Its version, which goes up with every change to the project or its crew. A client sends it back with a change,
/// so a change decided on an older reading is refused instead of made over what happened since.
/// </param>
/// <param name="Name">Its name.</param>
/// <param name="Number">Its number.</param>
/// <param name="UnitId">The unit it hangs at.</param>
/// <param name="State">Open or closed.</param>
/// <param name="OwnerSeat">The seat that owns it.</param>
/// <param name="Planned">The days it is planned for, or <see langword="null"/> when it is not planned.</param>
/// <param name="Via">How the caller sees it: through the crew, the organization, or as system work.</param>
/// <param name="ChangedBy">
/// Who changed the project last, as its row keeps it: a seat by its id, or the kind of whoever else it was. A
/// change to the crew changes the project, so it counts.
/// </param>
public sealed record ProjectOverview(
    ProjectId Id,
    long Version,
    string Name,
    string Number,
    OrganizationUnitId UnitId,
    ProjectState State,
    SeatId OwnerSeat,
    DateRange? Planned,
    ProjectVia? Via,
    ChangedBy ChangedBy);
