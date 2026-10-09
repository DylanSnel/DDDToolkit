using Mediator;

namespace Examples.Tenancy.Projects.Application.Overview.Queries;

/// <summary>
/// Those of some projects the caller may see, asked once for all of them: for whoever was handed ids and shows
/// what they stand for, such as a lookup that resolves the projects another module named.
/// </summary>
/// <remarks>
/// Nobody is refused for an id: a project the caller may not see with <see cref="RequiredKey"/> is left out of the
/// answer exactly as one that does not exist, so asking about an id tells nothing about it. The key is the filter
/// of the handler's one statement, not a check made before it.
/// </remarks>
/// <param name="Ids">The projects asked about, at most <see cref="MostProjects"/> different ones.</param>
public sealed record ProjectsById(IReadOnlyList<ProjectId> Ids) : IQuery<IReadOnlyList<ProjectOverview>>, IProjectsRequest
{
    /// <summary>The key that decides whether a project is there for the caller at all.</summary>
    public const string RequiredKey = ProjectKeys.View;

    /// <summary>The most projects one question is about.</summary>
    public const int MostProjects = 200;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.SeenWith<ProjectId>(RequiredKey);

    /// <summary>
    /// <paramref name="ids"/>, each once; refused when they are more than a question may be about. Every query of
    /// this feature that is asked about several projects reads its ids here, so all of them hold one cap.
    /// </summary>
    /// <exception cref="Exceptions.RefusalException"><c>projects.too-many-ids</c>, with <c>Max</c>.</exception>
    internal static IReadOnlyList<ProjectId> EachOnce(IReadOnlyList<ProjectId> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var asked = ids.Distinct().ToList();
        return asked.Count > MostProjects
            ? throw ProjectRefusals.Refuse(ProjectRefusals.TooManyIds, ("Max", MostProjects))
            : asked;
    }
}

/// <summary>
/// Answers <see cref="ProjectsById"/> in one statement, on a reading of this query's own, whatever the number of
/// ids; none when no id is asked about.
/// </summary>
/// <param name="reads">Where projects are read: a context per query.</param>
/// <param name="access">Who reaches which project.</param>
public sealed class ProjectsByIdHandler(IProjectReads reads, ProjectAccess access) : IQueryHandler<ProjectsById, IReadOnlyList<ProjectOverview>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException"><c>projects.too-many-ids</c>, with <c>Max</c>.</exception>
    public async ValueTask<IReadOnlyList<ProjectOverview>> Handle(ProjectsById query, CancellationToken cancellationToken)
    {
        var ids = ProjectsById.EachOnce(query.Ids);
        if (ids.Count == 0)
        {
            return [];
        }

        await using var reading = reads.Open();
        return await reading.ByIdsAsync(ids, access.ReachFor(ProjectsById.RequiredKey), cancellationToken);
    }
}
