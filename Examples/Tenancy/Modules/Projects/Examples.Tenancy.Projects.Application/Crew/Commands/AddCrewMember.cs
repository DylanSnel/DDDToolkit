using Mediator;

namespace Examples.Tenancy.Projects.Application.Crew.Commands;

/// <summary>
/// Puts an active seat, the caller's own included, on a project's crew, from now until a moment or for good. With
/// a project role, the seat is given that role for the same period in the same change; without one it is on the
/// crew with no role, which lets it see the project and nothing more.
/// </summary>
/// <remarks>
/// Whoever manages the crew may put anyone on it, themselves included, in any project role of the tenant in use,
/// without holding that role's permissions: on a crew a role gives only what acts on the project. That is this
/// application's rule, and the whole of it: the command requires <see cref="RequiredPermission"/> on the project, the
/// permission the projects' rules mark as the one that manages the crew, and nothing is checked for a caller who adds
/// their own seat.
/// </remarks>
/// <param name="Project">The project.</param>
/// <param name="Seat">The seat.</param>
/// <param name="Role">A project role to give it at once, for the same period. <see langword="null"/> for none.</param>
/// <param name="Until">When the membership ends, or <see langword="null"/> for no end.</param>
/// <param name="ExpectedVersion">
/// The version of the project the caller last read, or <see langword="null"/> to change it as it is now. Any other
/// version than the project's is a lost race: somebody changed it since, so the caller reads again and decides again.
/// </param>
[RequiresPermission(RequiredPermission)]
public sealed record AddCrewMember([OnResource] ProjectId Project, SeatId Seat, ProjectRoleId? Role, DateTimeOffset? Until, [ExpectedVersion] long? ExpectedVersion = null) : ICommand, IProjectsRequest
{
    /// <summary>The permission the caller holds on the project.</summary>
    public const string RequiredPermission = ProjectPermissionNames.ManageCrew;
}

/// <summary>
/// Handles <see cref="AddCrewMember"/> with one call to the crew manager, which takes the answer of the command's check,
/// admits the seat and the role, puts the seat on the crew of the project it loads, and saves. The seat that added it is
/// the caller's.
/// </summary>
/// <param name="crew">The projects' crew manager, which the Membership package's generator writes for <c>CrewMember</c>.</param>
public sealed class AddCrewMemberHandler(CrewMemberManager crew) : ICommandHandler<AddCrewMember>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.not-found</c>, <c>projects.seat-not-active</c>, <c>projects.role-not-for-members</c>,
    /// <c>projects.closed</c>, <c>projects.invalid-period</c> for an end that is not after now, and
    /// <c>projects.already-on-crew</c>.
    /// </exception>
    /// <exception cref="Exceptions.ConcurrencyConflictException">The project is at another version than the caller named, or was changed while this was saved.</exception>
    public async ValueTask<Unit> Handle(AddCrewMember command, CancellationToken cancellationToken)
    {
        await crew.AddAsync(command.Project, command.Seat, command.Role, command.Until, cancellationToken, command.ExpectedVersion);
        return Unit.Value;
    }
}
