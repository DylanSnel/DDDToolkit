using Examples.Tenancy.Projects.Application.Access.Queries;
using HotChocolate;
using HotChocolate.Types.Relay;
using Mediator;

namespace Examples.Tenancy.Projects.Api.Access.GraphQL;

/// <summary>What the caller asks about its own access to projects. A field sends the query its route sends, and nothing else.</summary>
internal static class AccessQueries
{
    /// <summary>
    /// Which of <paramref name="keys"/> the caller holds for the whole tenant and on each of
    /// <paramref name="projects"/>, for a page of them at once. A key the catalogue does not know is refused.
    /// </summary>
    [Query]
    public static async Task<ProjectKeySets> GetKeySetsAsync(
        IReadOnlyList<string> keys,
        [ID("Project")] IReadOnlyList<ProjectId> projects,
        [Service] ISender sender,
        CancellationToken cancellationToken)
        => await sender.Send(new KeysOnProjects(projects, keys), cancellationToken);
}
