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

/// <summary>Handles <see cref="ChangeProjectName"/>: loads the project its command names, renames it and saves.</summary>
/// <param name="store">Where projects are loaded and saved.</param>
public sealed class ChangeProjectNameHandler(IProjectStore store) : ICommandHandler<ChangeProjectName>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException"><c>projects.not-found</c>, <c>projects.closed</c>, <c>projects.name-invalid</c>.</exception>
    /// <exception cref="Exceptions.ConcurrencyConflictException">The project is at another version than the caller named, or was changed while this was saved.</exception>
    public async ValueTask<Unit> Handle(ChangeProjectName command, CancellationToken cancellationToken)
    {
        var project = await store.LoadAsync(command.Id, command.ExpectedVersion, cancellationToken)
            ?? throw ProjectRefusals.Of(ProjectRefusals.NotFound);

        project.Rename(command.Name);
        await store.SaveAsync(cancellationToken);
        return Unit.Value;
    }
}
