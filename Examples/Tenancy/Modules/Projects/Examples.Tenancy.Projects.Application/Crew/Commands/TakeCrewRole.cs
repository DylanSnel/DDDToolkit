using Mediator;

namespace Examples.Tenancy.Projects.Application.Crew.Commands;

/// <summary>
/// Takes a project role from a crew member, the caller included, who stays on the crew with whatever else it
/// holds there. The owner holds no project role, so there is none to take from it.
/// </summary>
/// <param name="Project">The project.</param>
/// <param name="Seat">The seat on its crew.</param>
/// <param name="Role">The project role to take.</param>
/// <param name="ExpectedVersion">
/// The version of the project the caller last read, or <see langword="null"/> to change it as it is now. Any other
/// version than the project's is a lost race: somebody changed it since, so the caller reads again and decides again.
/// </param>
[RequiresPermission(RequiredPermission)]
public sealed record TakeCrewRole([OnResource] ProjectId Project, SeatId Seat, ProjectRoleId Role, [ExpectedVersion] long? ExpectedVersion = null) : ICommand, IProjectsRequest
{
    /// <summary>The permission the caller holds on the project.</summary>
    public const string RequiredPermission = ProjectPermissionNames.ManageCrew;
}

/// <summary>Handles <see cref="TakeCrewRole"/> with one call to the crew manager, which takes the role on the project it loads, and saves.</summary>
/// <remarks>
/// Any role can be taken, one the tenant has since archived too: what was given once can always be taken back. A role
/// the caller takes from itself may be the very one its save needs, where the database checks every row; the crew
/// manager saves that change as this module's own work for the seat, since this command's check let it through.
/// </remarks>
/// <param name="crew">The projects' crew manager.</param>
public sealed class TakeCrewRoleHandler(CrewMemberManager crew) : ICommandHandler<TakeCrewRole>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.not-found</c>, <c>projects.closed</c>, <c>projects.member-not-found</c>, <c>projects.crew-role-not-found</c>.
    /// </exception>
    /// <exception cref="Exceptions.ConcurrencyConflictException">The project is at another version than the caller named, or was changed while this was saved.</exception>
    public async ValueTask<Unit> Handle(TakeCrewRole command, CancellationToken cancellationToken)
    {
        await crew.TakeRoleAsync(command.Project, command.Seat, command.Role, cancellationToken, command.ExpectedVersion);
        return Unit.Value;
    }
}
