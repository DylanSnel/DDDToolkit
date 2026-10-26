using Examples.Tenancy.Projects.Contracts.ValueObjects;

namespace Examples.Tenancy.Projects.Contracts.Gate;

/// <summary>
/// The one question another module may put to Projects: may the current caller do this to that project?
/// </summary>
/// <remarks>
/// A module that works on projects, as Inspections records inspections on them, asks here rather than reading
/// Projects' tables or repeating its rules. Projects answers from both of its ways in, the crew and the
/// organization, so the asking module never learns which one it was. Only the keys that act on a project come
/// through a crew: opening a project, naming its owner and managing the organization never do, whoever asks.
/// </remarks>
public interface IProjectGate
{
    /// <summary>What the caller may do with <paramref name="project"/> as far as <paramref name="key"/> goes.</summary>
    /// <param name="project">The project.</param>
    /// <param name="key">A key of the catalogue, of any module: Projects' own, or the asking module's.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <exception cref="ArgumentException">The catalogue does not know <paramref name="key"/>: a typo in code.</exception>
    Task<ProjectAnswer> AskAsync(ProjectId project, string key, CancellationToken cancellationToken);

    /// <summary>The most projects one question is about.</summary>
    const int MostProjects = 200;

    /// <summary>
    /// What the caller may do with each of <paramref name="projects"/> as far as <paramref name="key"/> goes,
    /// asked once for all of them: for a module that shows something of many projects at a time, which would
    /// otherwise ask once per project.
    /// </summary>
    /// <param name="projects">The projects, at most <see cref="MostProjects"/> different ones.</param>
    /// <param name="key">A key of the catalogue, of any module: Projects' own, or the asking module's.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>
    /// An answer for every project asked about that the caller may see. One it may not see is not in it, whether
    /// it belongs to another tenant, is out of the caller's reach or does not exist: the asking module passes it
    /// over, and learns nothing about it.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The catalogue does not know <paramref name="key"/>, or there are more than <see cref="MostProjects"/>
    /// projects: mistakes in the asking module's code.
    /// </exception>
    Task<IReadOnlyDictionary<ProjectId, ProjectAnswer>> AskAsync(IReadOnlyCollection<ProjectId> projects, string key, CancellationToken cancellationToken);
}
