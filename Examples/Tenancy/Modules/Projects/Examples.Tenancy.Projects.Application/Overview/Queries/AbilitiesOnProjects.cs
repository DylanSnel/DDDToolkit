using Examples.Tenancy.Projects.Application.Lifecycle.Commands;
using Mediator;

namespace Examples.Tenancy.Projects.Application.Overview.Queries;

/// <summary>
/// What the caller may do to each of some projects it may see, asked once for all of them: for a screen that shows
/// each action as open or not, and reads this only when it shows them.
/// </summary>
/// <remarks>
/// The abilities are answers, not checks: nothing is refused here for a key the caller does not hold, and whether
/// a command may run is asked again when it runs. They are decided in one place (<see cref="ProjectAbilities.For"/>),
/// from the very key each command declares, so a row of a list and a project on its own never disagree. A project
/// the caller may not see with <see cref="RequiredKey"/> is missing from the answer exactly as one that does not
/// exist.
/// </remarks>
/// <param name="Projects">The projects asked about, at most <see cref="ProjectsById.MostProjects"/> different ones.</param>
public sealed record AbilitiesOnProjects(IReadOnlyList<ProjectId> Projects) : IQuery<IReadOnlyDictionary<ProjectId, ProjectAbilities>>, IProjectsRequest
{
    /// <summary>The key that decides whether a project is there for the caller at all.</summary>
    public const string RequiredKey = ProjectKeys.View;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.SeenWith<ProjectId>(RequiredKey);
}

/// <summary>
/// Answers <see cref="AbilitiesOnProjects"/> in one statement, on a reading of this query's own, whatever the
/// number of projects: each project's state, every key an ability stands for that the caller holds on it, and
/// whether there is anywhere to move a project to. None when no project is asked about.
/// </summary>
/// <param name="reads">Where projects are read: a context per query.</param>
/// <param name="access">Which keys the caller holds where.</param>
public sealed class AbilitiesOnProjectsHandler(IProjectReads reads, ProjectAccess access) : IQueryHandler<AbilitiesOnProjects, IReadOnlyDictionary<ProjectId, ProjectAbilities>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException"><c>projects.too-many-ids</c>, with <c>Max</c>.</exception>
    public async ValueTask<IReadOnlyDictionary<ProjectId, ProjectAbilities>> Handle(AbilitiesOnProjects query, CancellationToken cancellationToken)
    {
        var projects = ProjectsById.EachOnce(query.Projects);
        if (projects.Count == 0)
        {
            return new Dictionary<ProjectId, ProjectAbilities>();
        }

        await using var reading = reads.Open();

        // Moving needs somewhere to move to: the units where the caller may open a project, as a set not yet read,
        // so whether there is one is answered inside the statement that reads the keys.
        var standing = await reading.StandingOnAsync(
            projects,
            access.KeysReachFor(ProjectAbilities.Keys),
            access.Questions(reading).UnitsWhereIHold(MoveProjectToUnit.DestinationKey),
            cancellationToken);

        return standing.ToDictionary(
            project => project.Key,
            project => ProjectAbilities.For(project.Value.State, project.Value.Held, project.Value.HoldsAtAUnit));
    }
}
