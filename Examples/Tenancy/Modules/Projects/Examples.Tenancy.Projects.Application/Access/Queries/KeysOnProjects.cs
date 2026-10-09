using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Projects.Application.Access.Queries;

/// <summary>
/// Which of some keys the caller holds on each of some projects, and which of them it holds for the whole tenant:
/// for a client that shows many projects and what its user may do with each, asked once for all of them.
/// </summary>
/// <remarks>
/// It asks about the caller only, so it refuses nobody for a key: holding none is the answer. A project the caller
/// may not see with <see cref="RequiredKey"/> is left out of the answer exactly as one that does not exist, so
/// asking about an id tells nothing about it. What is answered is for showing: whether a command may run is asked
/// again when it runs.
/// </remarks>
/// <param name="Projects">The projects asked about, at most <see cref="MostProjects"/> different ones.</param>
/// <param name="Keys">Permission keys, as the request names them.</param>
public sealed record KeysOnProjects(IReadOnlyCollection<ProjectId> Projects, IReadOnlyCollection<string> Keys) : IQuery<ProjectKeySets>, IProjectsRequest
{
    /// <summary>The key that decides whether a project is there for the caller at all.</summary>
    public const string RequiredKey = ProjectKeys.View;

    /// <summary>The most projects one question is about.</summary>
    public const int MostProjects = 200;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.SeenWith<ProjectId>(RequiredKey);
}

/// <summary>The keys a caller holds: for the whole tenant, and on each project asked about.</summary>
/// <param name="HeldAtRoot">Those of the keys asked about that the caller holds at the tenant's root, and so everywhere.</param>
/// <param name="OnProjects">
/// For each project asked about that the caller sees and holds at least one of the keys on: those keys, through its
/// crew or through the organization, the projects by their ids' value. A project that is missing here is one the
/// caller holds none of them on, or does not see.
/// </param>
public sealed record ProjectKeySets(IReadOnlyList<string> HeldAtRoot, IReadOnlyList<ProjectKeySet> OnProjects);

/// <summary>What a caller holds on a single project, of the keys it asked about.</summary>
/// <param name="Project">The project.</param>
/// <param name="Keys">The keys held on it, of those asked about, in the order of their text.</param>
public sealed record ProjectKeySet(ProjectId Project, IReadOnlyList<string> Keys);

/// <summary>
/// Answers <see cref="KeysOnProjects"/> in two statements, on a reading of this query's own: the keys held at the
/// root, and the keys held on the projects, whatever their number.
/// </summary>
/// <param name="reads">Where projects are read: a context per query.</param>
/// <param name="access">Which keys the caller holds where.</param>
/// <param name="catalogue">The permission keys the application knows, for keys that come from outside.</param>
public sealed class KeysOnProjectsHandler(IProjectReads reads, ProjectAccess access, TenancyCatalogue catalogue) : IQueryHandler<KeysOnProjects, ProjectKeySets>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.too-many-ids</c>, with <c>Max</c>; <c>tenancy.unknown-permission</c>, with the <c>Keys</c> the
    /// catalogue does not know.
    /// </exception>
    public async ValueTask<ProjectKeySets> Handle(KeysOnProjects query, CancellationToken cancellationToken)
    {
        var projects = query.Projects.Distinct().ToList();
        if (projects.Count > KeysOnProjects.MostProjects)
        {
            throw ProjectRefusals.Refuse(ProjectRefusals.TooManyIds, ("Max", KeysOnProjects.MostProjects));
        }

        var keys = KeysHeldAtRoot.Known(query.Keys, catalogue);

        await using var reading = reads.Open();
        var atRoot = await access.HeldTenantWideAsync(reading, keys, cancellationToken);

        if (projects.Count == 0 || keys.Count == 0)
        {
            return new ProjectKeySets(atRoot, []);
        }

        // One order, the same every time: the projects by their ids' value, each one's keys by their text.
        var held = await reading.KeysOnAsync(projects, access.KeysReachFor(keys), cancellationToken);
        return new ProjectKeySets(
            atRoot,
            [.. held.OrderBy(project => project.Key.Value).Select(project => new ProjectKeySet(project.Key, [.. project.Value.Order(StringComparer.Ordinal)]))]);
    }
}
