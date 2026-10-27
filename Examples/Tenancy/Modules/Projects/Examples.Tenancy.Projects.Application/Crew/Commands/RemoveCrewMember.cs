using Mediator;

namespace Examples.Tenancy.Projects.Application.Crew.Commands;

/// <summary>Takes a seat off a project's crew, with every role it holds there. The owner stays until somebody else is named owner.</summary>
/// <param name="Project">The project.</param>
/// <param name="Seat">The seat on its crew.</param>
/// <param name="ExpectedVersion">
/// The version of the project the caller last read, or <see langword="null"/> to change it as it is now. Any other
/// version than the project's is a lost race: somebody changed it since, so the caller reads again and decides again.
/// </param>
[RequiresPermission(RequiredPermission)]
public sealed record RemoveCrewMember([OnResource] ProjectId Project, SeatId Seat, [ExpectedVersion] long? ExpectedVersion = null) : ICommand, IProjectsRequest
{
    /// <summary>The permission the caller holds on the project.</summary>
    public const string RequiredPermission = ProjectPermissionNames.ManageCrew;
}

/// <summary>Handles <see cref="RemoveCrewMember"/> with one call to the crew manager, which takes the seat off the crew of the project it loads, and saves.</summary>
/// <remarks>
/// A seat that takes itself off may lose with its place the very right the save needs, where the database checks every
/// row. The crew manager saves that change as this module's own work for the seat, since this command's check let it
/// through: nothing here says so.
/// </remarks>
/// <param name="crew">The projects' crew manager.</param>
public sealed class RemoveCrewMemberHandler(CrewMemberManager crew) : ICommandHandler<RemoveCrewMember>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.not-found</c>, <c>projects.closed</c>, <c>projects.member-not-found</c>, <c>projects.owner-protected</c>.
    /// </exception>
    /// <exception cref="Exceptions.ConcurrencyConflictException">The project is at another version than the caller named, or was changed while this was saved.</exception>
    public async ValueTask<Unit> Handle(RemoveCrewMember command, CancellationToken cancellationToken)
    {
        await crew.RemoveAsync(command.Project, command.Seat, cancellationToken, command.ExpectedVersion);
        return Unit.Value;
    }
}
