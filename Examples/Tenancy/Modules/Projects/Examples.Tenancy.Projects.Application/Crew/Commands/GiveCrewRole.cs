using Mediator;

namespace Examples.Tenancy.Projects.Application.Crew.Commands;

/// <summary>
/// Gives a crew member, the caller included, a project role on the project, from now until a moment or for good,
/// next to whatever roles the member holds there already.
/// </summary>
/// <remarks>
/// Giving a project role is positional: whoever manages the crew gives any of the tenant's project roles in use,
/// to anyone on the crew, themselves included, without holding that role's permissions. That is this application's rule,
/// declared as <see cref="RequiredPermission"/> on the project. A seat holds a role once on a crew, so one it still holds
/// is refused; a role's period is its own, and may end before the membership does, or after it, where it then
/// counts for nothing.
/// </remarks>
/// <param name="Project">The project.</param>
/// <param name="Seat">The seat on its crew.</param>
/// <param name="Role">The project role.</param>
/// <param name="Until">When the role ends, or <see langword="null"/> for no end.</param>
/// <param name="ExpectedVersion">
/// The version of the project the caller last read, or <see langword="null"/> to change it as it is now. Any other
/// version than the project's is a lost race: somebody changed it since, so the caller reads again and decides again.
/// </param>
[RequiresPermission(RequiredPermission)]
public sealed record GiveCrewRole([OnResource] ProjectId Project, SeatId Seat, ProjectRoleId Role, DateTimeOffset? Until, [ExpectedVersion] long? ExpectedVersion = null) : ICommand, IProjectsRequest
{
    /// <summary>The permission the caller holds on the project.</summary>
    public const string RequiredPermission = ProjectPermissionNames.ManageCrew;
}

/// <summary>
/// Handles <see cref="GiveCrewRole"/> with one call to the crew manager, which admits the role, gives it on the project
/// it loads, and saves. The seat that gave it is the caller's.
/// </summary>
/// <param name="crew">The projects' crew manager.</param>
public sealed class GiveCrewRoleHandler(CrewMemberManager crew) : ICommandHandler<GiveCrewRole>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.not-found</c>, <c>projects.role-not-for-members</c>, <c>projects.closed</c>,
    /// <c>projects.invalid-period</c> for an end that is not after now, <c>projects.member-not-found</c>,
    /// <c>projects.crew-role-held</c>, and <c>projects.owner-protected</c> for the owner, who holds no project role.
    /// </exception>
    /// <exception cref="Exceptions.ConcurrencyConflictException">The project is at another version than the caller named, or was changed while this was saved.</exception>
    public async ValueTask<Unit> Handle(GiveCrewRole command, CancellationToken cancellationToken)
    {
        await crew.GiveRoleAsync(command.Project, command.Seat, command.Role, command.Until, cancellationToken, command.ExpectedVersion);
        return Unit.Value;
    }
}
