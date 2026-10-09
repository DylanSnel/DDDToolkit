using Mediator;

namespace Examples.Tenancy.Projects.Application.Overview.Queries;

/// <summary>One project the caller may see, with its version and how the caller sees it.</summary>
/// <remarks>
/// For whoever asks about one project by name and wants a refusal when it is not there: a route. Asked for several
/// at once, where a project out of reach is simply left out, it is <see cref="ProjectsById"/>.
/// </remarks>
/// <param name="Id">The project.</param>
public sealed record ProjectDetail(ProjectId Id) : IQuery<ProjectOverview>, IProjectsRequest
{
    /// <summary>The key the caller holds on the project.</summary>
    public const string RequiredKey = ProjectKeys.View;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.On(RequiredKey, Id);
}

/// <summary>Answers <see cref="ProjectDetail"/> in one statement, on a reading of this query's own.</summary>
/// <param name="reads">Where projects are read: a context per query.</param>
/// <param name="access">Who reaches which project.</param>
public sealed class ProjectDetailHandler(IProjectReads reads, ProjectAccess access) : IQueryHandler<ProjectDetail, ProjectOverview>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException"><c>projects.not-found</c> when the project is gone since the access check.</exception>
    public async ValueTask<ProjectOverview> Handle(ProjectDetail query, CancellationToken cancellationToken)
    {
        await using var reading = reads.Open();

        var found = await reading.ByIdsAsync([query.Id], access.ReachFor(ProjectDetail.RequiredKey), cancellationToken);
        return found.Count == 1 ? found[0] : throw ProjectRefusals.Refuse(ProjectRefusals.NotFound);
    }
}
