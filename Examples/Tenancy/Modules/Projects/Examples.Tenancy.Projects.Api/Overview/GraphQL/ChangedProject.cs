using Examples.Tenancy.Projects.Application.Overview;
using Examples.Tenancy.Projects.Application.Overview.Queries;
using Mediator;

namespace Examples.Tenancy.Projects.Api.Overview.GraphQL;

/// <summary>
/// What a mutation answers: the project as it is after the change, read once the command has run.
/// </summary>
/// <remarks>
/// The project is read directly, not through its loader: a loader remembers what it loaded for a request, and a
/// project read before the change would be answered after it. The project's crew and what the caller may do to it
/// are fields like in any other answer, read only when the payload selects them.
/// </remarks>
internal static class ChangedProject
{
    /// <summary>
    /// The project, or nothing when it is no longer the caller's to see: whoever takes themselves off a crew, or
    /// moves a project out of their reach, changed it all the same, so that is no error.
    /// </summary>
    /// <param name="id">The project that was changed.</param>
    /// <param name="sender">Sends the query.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    public static async Task<ProjectOverview?> ReadAsync(ProjectId id, ISender sender, CancellationToken cancellationToken)
        => (await sender.Send(new ProjectsById([id]), cancellationToken)).SingleOrDefault();
}
