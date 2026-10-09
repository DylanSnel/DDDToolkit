using Mediator;

namespace Examples.Tenancy.Projects.Application.Lifecycle.Commands;

/// <summary>Reopens a closed project. Whoever may close it may reopen it: the same key.</summary>
/// <param name="Id">The project.</param>
/// <param name="ExpectedVersion">
/// The version of the project the caller last read, or <see langword="null"/> to change it as it is now. Any other
/// version than the project's is a lost race: somebody changed it since, so the caller reads again and decides again.
/// </param>
public sealed record ReopenProject(ProjectId Id, long? ExpectedVersion = null) : ICommand, IProjectsRequest
{
    /// <summary>The key the caller holds on the project: the one closing it needs.</summary>
    public const string RequiredKey = CloseProject.RequiredKey;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.On(RequiredKey, Id, ExpectedVersion);
}

/// <summary>Handles <see cref="ReopenProject"/>: loads the project its command names, reopens it and saves.</summary>
/// <param name="store">Where projects are loaded and saved.</param>
public sealed class ReopenProjectHandler(IProjectStore store) : ICommandHandler<ReopenProject>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException"><c>projects.not-found</c>, <c>projects.not-closed</c>.</exception>
    /// <exception cref="Exceptions.ConcurrencyConflictException">The project is at another version than the caller named, or was changed while this was saved.</exception>
    public async ValueTask<Unit> Handle(ReopenProject command, CancellationToken cancellationToken)
    {
        var project = await store.LoadAsync(command.Id, command.ExpectedVersion, cancellationToken)
            ?? throw ProjectRefusals.Refuse(ProjectRefusals.NotFound);

        project.Reopen();
        await store.SaveAsync(cancellationToken);
        return Unit.Value;
    }
}
