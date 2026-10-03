using Mediator;

namespace Examples.Tenancy.Projects.Application.Lifecycle.Commands;

/// <summary>Renames a project.</summary>
/// <param name="Id">The project.</param>
/// <param name="Name">Its new name.</param>
/// <param name="ExpectedVersion">
/// The version of the project the caller last read, or <see langword="null"/> to change it as it is now. Any other
/// version than the project's is a lost race: somebody changed it since, so the caller reads again and decides again.
/// </param>
public sealed record ChangeProjectName(ProjectId Id, string Name, long? ExpectedVersion = null) : ICommand, IProjectsRequest
{
    /// <summary>The key the caller holds on the project.</summary>
    public const string RequiredKey = ProjectKeys.Edit;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.On(RequiredKey, Id, ExpectedVersion);
}

/// <summary>Handles <see cref="ChangeProjectName"/>: loads the project that was checked, renames it and saves.</summary>
/// <param name="store">Where projects are loaded and saved.</param>
/// <param name="checkedProject">What the access check read of the project.</param>
public sealed class ChangeProjectNameHandler(IProjectStore store, Checked<MemberHold<ProjectId>> checkedProject) : ICommandHandler<ChangeProjectName>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException"><c>projects.not-found</c>, <c>projects.closed</c>, <c>projects.name-invalid</c>.</exception>
    /// <exception cref="Exceptions.ConcurrencyConflictException">The project was changed since the access check, or while this was saved.</exception>
    public async ValueTask<Unit> Handle(ChangeProjectName command, CancellationToken cancellationToken)
    {
        var seen = checkedProject.TakeFor(command);
        var project = await store.LoadAsync(seen.Resource, seen.Version, cancellationToken)
            ?? throw ProjectRefusals.Of(ProjectRefusals.NotFound);

        project.Rename(command.Name);
        await store.SaveAsync(cancellationToken);
        return Unit.Value;
    }
}
