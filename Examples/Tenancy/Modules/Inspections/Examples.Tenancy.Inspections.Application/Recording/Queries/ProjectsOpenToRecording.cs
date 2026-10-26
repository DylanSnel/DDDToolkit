using Examples.Tenancy.Inspections.Application.Recording.Commands;
using Mediator;

namespace Examples.Tenancy.Inspections.Application.Recording.Queries;

/// <summary>
/// On which of several projects the caller may record an inspection now: for a client that shows projects and a
/// "record an inspection" button on each, asked once for all of them.
/// </summary>
/// <remarks>
/// An answer, not a check: it asks about the caller only and refuses nobody, and whether the command may run is
/// asked again when it runs. A project is in the answer when the caller holds <see cref="RequiredKey"/> on it and
/// it is open, which is what the command's own check requires; one the caller may not see is left out exactly as
/// one that does not exist. Projects' gate is asked once, about all of them, by the handler.
/// </remarks>
/// <param name="Projects">The projects, at most <see cref="InspectionsOfProjects.MostProjects"/> different ones.</param>
public sealed record ProjectsOpenToRecording(IReadOnlyList<ProjectId> Projects) : IQuery<IReadOnlySet<ProjectId>>, IInspectionsRequest
{
    /// <summary>The key asked about: the one the command that records declares, so a command whose key changes takes this answer with it.</summary>
    public const string RequiredKey = RecordInspection.RequiredKey;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new InspectionsRequirement.OnProjectsInReach(RequiredKey, Projects);
}

/// <summary>Answers <see cref="ProjectsOpenToRecording"/> from what Projects' gate answers, asked once for all of them.</summary>
/// <param name="projects">Projects' answer to "may the caller do this to those projects".</param>
public sealed class ProjectsOpenToRecordingHandler(IProjectGate projects) : IQueryHandler<ProjectsOpenToRecording, IReadOnlySet<ProjectId>>
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlySet<ProjectId>> Handle(ProjectsOpenToRecording query, CancellationToken cancellationToken)
        => (await projects.AskAsync(query.Projects, ProjectsOpenToRecording.RequiredKey, cancellationToken))
            .Where(answer => answer.Value is { Allowed: true, Closed: false })
            .Select(answer => answer.Key)
            .ToHashSet();
}
