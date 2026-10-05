using Mediator;

namespace Examples.Tenancy.Projects.Application.Crew.Commands;

/// <summary>Takes a seat off a project's crew, with every role it holds there. The owner stays until somebody else is named owner.</summary>
/// <param name="Project">The project.</param>
/// <param name="Seat">The seat on its crew.</param>
/// <param name="ExpectedVersion">
/// The version of the project the caller last read, or <see langword="null"/> to change it as it is now. Any other
/// version than the project's is a lost race: somebody changed it since, so the caller reads again and decides again.
/// </param>
public sealed record RemoveCrewMember(ProjectId Project, SeatId Seat, long? ExpectedVersion = null) : ICommand, IProjectsRequest
{
    /// <summary>The key the caller holds on the project.</summary>
    public const string RequiredKey = ProjectKeys.ManageCrew;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.On(RequiredKey, Project, ExpectedVersion);
}

/// <summary>Handles <see cref="RemoveCrewMember"/>: loads the project its command names, takes the seat off its crew and saves.</summary>
/// <param name="store">Where projects are loaded and saved.</param>
/// <param name="answers">Tenancy's answers about the current caller: whether the seat is the caller's own.</param>
public sealed class RemoveCrewMemberHandler(IProjectStore store, SampleAnswers answers) : ICommandHandler<RemoveCrewMember>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.not-found</c>, <c>projects.member-not-found</c>, <c>projects.owner-protected</c>, <c>projects.closed</c>.
    /// </exception>
    /// <exception cref="Exceptions.ConcurrencyConflictException">The project is at another version than the caller named, or was changed while this was saved.</exception>
    public async ValueTask<Unit> Handle(RemoveCrewMember command, CancellationToken cancellationToken)
    {
        var project = await store.LoadAsync(command.Project, command.ExpectedVersion, cancellationToken)
            ?? throw ProjectRefusals.Of(ProjectRefusals.NotFound);

        project.RemoveFromCrew(command.Seat);

        // The caller's own place may be what this very change was allowed by, once this command's check let it
        // through: see OwnPlaceOnTheCrew. Either way it writes this project and nothing an earlier command may
        // have left in the unit of work.
        using (OwnPlaceOnTheCrew.BeginSave(command, command.Seat, answers.RequireTenant()))
        {
            await store.SaveOnlyAsync(project, cancellationToken);
        }

        return Unit.Value;
    }
}
