using Mediator;

namespace Examples.Tenancy.Projects.Application.Crew.Queries;

/// <summary>
/// The crew of one project the caller may see, on its own: who is on it, for which period, and, for a caller who
/// manages the crew, the roles each member holds there, each with its own period.
/// </summary>
/// <remarks>
/// For a screen that shows a crew and nothing else of the project. It answers what a project's crew reads as
/// everywhere, in the same order, without the project around it and without asking what the caller may do.
/// <para>
/// Seeing a crew is seeing its project, so the key is <see cref="RequiredKey"/>. It is the filter of the
/// handler's own statement, not a check made before it: the whole query is one statement, and a project the
/// caller does not reach is not found, exactly as one that does not exist.
/// </para>
/// <para>
/// The members' roles take a second key, <see cref="CrewOverview.RolesKey"/>, held on the project. Whether the
/// caller holds it is read in the same statement, and a caller who does not is answered every member without
/// its roles: the rule is held here, where the data is read, so no route and no field has to remember it.
/// </para>
/// </remarks>
/// <param name="Project">The project.</param>
public sealed record AllCrewMembers(ProjectId Project) : IQuery<IReadOnlyList<CrewOverview>>, IProjectsRequest
{
    /// <summary>The key that decides whether the project, and with it its crew, is there for the caller at all.</summary>
    public const string RequiredKey = ProjectKeys.View;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.SeenWith<ProjectId>(RequiredKey);
}

/// <summary>
/// Answers <see cref="AllCrewMembers"/> in one statement, on a reading of this query's own: the crew of the
/// project within the caller's reach, with the reach a subquery of it.
/// </summary>
/// <param name="reads">Where projects are read: a context per query.</param>
/// <param name="access">Who reaches which project.</param>
public sealed class AllCrewMembersHandler(IProjectReads reads, ProjectAccess access) : IQueryHandler<AllCrewMembers, IReadOnlyList<CrewOverview>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException"><c>projects.not-found</c> for a project the caller may not see.</exception>
    public async ValueTask<IReadOnlyList<CrewOverview>> Handle(AllCrewMembers query, CancellationToken cancellationToken)
    {
        // Each reach is a set not yet read; the reading puts both into the statement that reads the crew.
        var (see, manage) = access.SeenAndHeld(CrewOverview.RolesKey);

        await using var reading = reads.Open();
        var crews = await reading.CrewsOfAsync([query.Project], see, manage, cancellationToken);

        return crews.Count == 1 ? CrewOverviews.From(crews[0], see.Now) : throw ProjectRefusals.Refuse(ProjectRefusals.NotFound);
    }
}
