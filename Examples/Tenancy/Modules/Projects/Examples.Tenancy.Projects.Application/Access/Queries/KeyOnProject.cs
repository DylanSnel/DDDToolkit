using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Projects.Application.Access.Queries;

/// <summary>
/// Whether the caller holds a key on a project it may see, and through what: for a screen that asks about one
/// key the commands have no single action for.
/// </summary>
/// <remarks>
/// It asks about the caller only, and only about a project the caller sees with <see cref="RequiredKey"/>, so it
/// refuses nobody for the key asked about: not holding it is the answer. The key comes from outside, so the order
/// is the caller, then the key, then whether the caller sees the project: a project the caller cannot see is not
/// found, as everywhere, whatever the key.
/// </remarks>
/// <param name="Project">The project.</param>
/// <param name="Key">A permission key, as the request names it.</param>
public sealed record KeyOnProject(ProjectId Project, string Key) : IQuery<ProjectKeyHeld>, IProjectsRequest
{
    /// <summary>The key that decides whether the project is there for the caller at all.</summary>
    public const string RequiredKey = ProjectKeys.View;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.SeenWith<ProjectId>(RequiredKey);
}

/// <summary>Whether a caller holds a key on a project, and how it reaches the project when it does.</summary>
/// <param name="Allowed">Whether the key asked about is the caller's on that project.</param>
/// <param name="Via">Through what, or <see langword="null"/> when it does not hold it.</param>
public sealed record ProjectKeyHeld(bool Allowed, ProjectVia? Via);

/// <summary>Answers <see cref="KeyOnProject"/> in one statement, the access questions' own.</summary>
/// <param name="access">How the caller holds a key on a project it may see.</param>
/// <param name="catalogue">The permission keys the application knows, for a key that comes from outside.</param>
public sealed class KeyOnProjectHandler(ProjectAccess access, TenancyCatalogue catalogue) : IQueryHandler<KeyOnProject, ProjectKeyHeld>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.unknown-permission</c> for a key the catalogue does not know; <c>projects.not-found</c> for a
    /// project the caller may not see.
    /// </exception>
    public async ValueTask<ProjectKeyHeld> Handle(KeyOnProject query, CancellationToken cancellationToken)
    {
        // The questions treat an unknown key as a bug in the code that asks. Here it is only a wrong request.
        if (!catalogue.Knows(query.Key))
        {
            throw TenancyRefusals.Of(TenancyRefusals.UnknownPermission, ("Keys", query.Key));
        }

        var via = await access.ViaAsync(query.Project, query.Key, cancellationToken);

        return new ProjectKeyHeld(via is not null, via);
    }
}
