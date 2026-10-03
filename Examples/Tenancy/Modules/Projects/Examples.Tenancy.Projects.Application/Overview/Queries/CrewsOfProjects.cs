using DDDToolkit.Supporting.Tenancy.Access;
using Examples.Tenancy.Projects.Application.Crew;
using Mediator;

namespace Examples.Tenancy.Projects.Application.Overview.Queries;

/// <summary>
/// The crews of some projects the caller may see, asked once for all of them: for whoever shows a list of
/// projects with their crews, and reads the crews only when they are shown.
/// </summary>
/// <remarks>
/// Seeing a crew is seeing its project, so the key is <see cref="RequiredKey"/>, and it is the filter of the
/// handler's one statement: a project the caller may not see is missing from the answer exactly as one that does
/// not exist. The members' roles take <see cref="CrewOverview.RolesKey"/> on the project, read in that same
/// statement: on a crew the caller does not manage, each member is answered without them.
/// </remarks>
/// <param name="Projects">The projects asked about, at most <see cref="ProjectsById.MostProjects"/> different ones.</param>
public sealed record CrewsOfProjects(IReadOnlyList<ProjectId> Projects) : IQuery<IReadOnlyDictionary<ProjectId, ProjectCrew>>, IProjectsRequest
{
    /// <summary>The key that decides whether a project, and with it its crew, is there for the caller at all.</summary>
    public const string RequiredKey = ProjectKeys.View;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.SeenWith<ProjectId>(RequiredKey);
}

/// <summary>The crew of one project, and the caller's own place on it.</summary>
/// <param name="Members">
/// The crew: the owner first, then by when each membership starts, then by seat. Each with its roles for a caller
/// who manages the crew, and without them for anybody else.
/// </param>
/// <param name="MyRoleIds">
/// The roles the caller holds on the crew now, by their ids, ordered by the id's value: each role whose own period
/// applies, in a membership that applies. Empty for a caller who is not on the crew, or is on it with no role.
/// </param>
public sealed record ProjectCrew(IReadOnlyList<CrewOverview> Members, IReadOnlyList<ProjectRoleId> MyRoleIds);

/// <summary>
/// Answers <see cref="CrewsOfProjects"/> in one statement, on a reading of this query's own, whatever the number
/// of projects; none when no project is asked about.
/// </summary>
/// <param name="reads">Where projects are read: a context per query.</param>
/// <param name="access">Who reaches which project.</param>
/// <param name="answers">Tenancy's answers about the current caller: its seat, whose own roles are answered.</param>
public sealed class CrewsOfProjectsHandler(IProjectReads reads, ProjectAccess access, SampleAnswers answers) : IQueryHandler<CrewsOfProjects, IReadOnlyDictionary<ProjectId, ProjectCrew>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException"><c>projects.too-many-ids</c>, with <c>Max</c>.</exception>
    public async ValueTask<IReadOnlyDictionary<ProjectId, ProjectCrew>> Handle(CrewsOfProjects query, CancellationToken cancellationToken)
    {
        var projects = ProjectsById.EachOnce(query.Projects);
        if (projects.Count == 0)
        {
            return new Dictionary<ProjectId, ProjectCrew>();
        }

        // Each reach is a set not yet read; the reading puts both into the statement that reads the crews.
        var (see, manage) = access.SeenAndHeld(CrewOverview.RolesKey);

        await using var reading = reads.Open();
        var crews = await reading.CrewsOfAsync(projects, see, manage, cancellationToken);

        // Only a seat is on a crew: system work that acts for a seat holds no role of its own.
        var caller = answers.Caller;
        SeatId? mine = caller.Kind == TenancyCallerKind.Seat ? caller.Seat : null;

        // The caller's own roles are read from what the reading returned, before the roles of a crew the caller
        // does not manage are left out of its members.
        return crews.ToDictionary(
            crew => crew.Project,
            crew => new ProjectCrew(
                CrewOverviews.Of(crew, see.Now),
                mine is { } seat ? CrewOverviews.RolesHeldNow(crew, seat, see.Now) : []));
    }
}
