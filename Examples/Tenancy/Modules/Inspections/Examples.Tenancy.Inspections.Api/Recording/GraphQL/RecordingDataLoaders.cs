using Examples.Tenancy.Inspections.Application.Recording;
using Examples.Tenancy.Inspections.Application.Recording.Queries;
using GreenDonut;
using GreenDonut.Data;
using Mediator;

namespace Examples.Tenancy.Inspections.Api.Recording.GraphQL;

/// <summary>
/// The data loaders behind the fields Inspections adds to a project, written by HotChocolate's generator from
/// these methods: the projects of an answer are collected into a batch, and a batch is asked about once.
/// </summary>
/// <remarks>
/// A loader decides nothing either: it sends a query that is about many projects, and that query's access check
/// asks Projects' gate once about all of them. So the projects of one batch cost one question and one statement,
/// never one of each per project, and a page of projects is as a rule one batch. A project the query leaves out,
/// because the caller may not see it, is a key without a value: the field answers nothing for it.
/// <para>
/// The projects are handed over in parts of at most what one question is about. A client's page of projects is
/// smaller than that, so this splits nothing a client asks for; it keeps a request that names projects by the
/// thousand from becoming one statement.
/// </para>
/// </remarks>
internal static class RecordingDataLoaders
{
    /// <summary>
    /// The same page of the inspections of each project: <paramref name="paging"/> is what the field was asked
    /// with, which the resolver gives the loader with <c>With(paging)</c>.
    /// </summary>
    [DataLoader]
    public static async Task<IReadOnlyDictionary<ProjectId, Page<InspectionOverview>>> GetInspectionsByProjectIdAsync(
        IReadOnlyList<ProjectId> projects,
        PagingArguments paging,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var pages = new Dictionary<ProjectId, Page<InspectionOverview>>();
        foreach (var part in projects.Chunk(InspectionsOfProjects.MostProjects))
        {
            foreach (var (project, page) in await sender.Send(new InspectionsOfProjects(part, paging), cancellationToken))
            {
                pages[project] = page;
            }
        }

        return pages;
    }

    /// <summary>Whether the caller may record an inspection on each project now.</summary>
    [DataLoader]
    public static async Task<IReadOnlyDictionary<ProjectId, bool>> GetOpenToRecordingByProjectIdAsync(
        IReadOnlyList<ProjectId> projects,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var open = new Dictionary<ProjectId, bool>();
        foreach (var part in projects.Chunk(InspectionsOfProjects.MostProjects))
        {
            var mayRecord = await sender.Send(new ProjectsOpenToRecording(part), cancellationToken);
            foreach (var project in part)
            {
                open[project] = mayRecord.Contains(project);
            }
        }

        return open;
    }
}
