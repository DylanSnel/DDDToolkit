using DDDToolkit.Supporting.Membership;
using Mediator;

namespace Examples.Tenancy.Projects.Application.Ownership.Commands;

/// <summary>
/// Names another owner, an active seat, who goes on the crew with no end, when not on it yet, and holds no project
/// role there: the owner holds what an owner does by owning the project, so the roles it held on the crew go. The
/// old owner becomes what the command says: a crew member in <see cref="FormerOwnerRole"/>, or, when it names none,
/// nobody on the crew.
/// </summary>
/// <remarks>
/// Only the organization names an owner: <see cref="RequiredPermission"/> is held at the project's unit or above it,
/// never through the crew, whatever role is on it, and not by owning the project either
/// (<see cref="Access.ProjectPermissions.ChangeOwner"/>). So the owner cannot hand the project on. The projects'
/// rules mark the permission as the one that names the owner, so the crew manager requires it, and the database holds a
/// caller to it as well.
/// </remarks>
/// <param name="Id">The project.</param>
/// <param name="Seat">The seat that is to own it.</param>
/// <param name="FormerOwnerRole">
/// The project role the old owner stays on the crew in, one of the tenant's in use; or <see langword="null"/> for the
/// old owner to leave the crew. Nothing is assumed for it: the crew lead's role is a role like any other.
/// </param>
/// <param name="ExpectedVersion">
/// The version of the project the caller last read, or <see langword="null"/> to change it as it is now. Any other
/// version than the project's is a lost race: somebody changed it since, so the caller reads again and decides again.
/// </param>
[RequiresPermission(RequiredPermission)]
public sealed record ChangeProjectOwner([OnResource] ProjectId Id, SeatId Seat, ProjectRoleId? FormerOwnerRole, [ExpectedVersion] long? ExpectedVersion = null) : ICommand, IProjectsRequest
{
    /// <summary>The permission the caller holds on the project, through the organization.</summary>
    public const string RequiredPermission = ProjectPermissionNames.ChangeOwner;
}

/// <summary>
/// Handles <see cref="ChangeProjectOwner"/> with one call to the crew manager, which takes the answer of the command's
/// check, admits the seat and the role the old owner stays in, names the owner of the project it loads, and saves.
/// </summary>
/// <param name="crew">The projects' crew manager.</param>
public sealed class ChangeProjectOwnerHandler(CrewMemberManager crew) : ICommandHandler<ChangeProjectOwner>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.not-found</c>, <c>projects.seat-not-active</c>, <c>projects.role-not-for-members</c> for the old
    /// owner's role, <c>projects.closed</c>, <c>projects.already-owner</c>.
    /// </exception>
    /// <exception cref="Exceptions.ConcurrencyConflictException">The project is at another version than the caller named, or was changed while this was saved.</exception>
    public async ValueTask<Unit> Handle(ChangeProjectOwner command, CancellationToken cancellationToken)
    {
        FormerOwner<ProjectRoleId> formerOwner = command.FormerOwnerRole is { } role ? FormerOwner.StaysAs(role) : FormerOwner.Leaves;
        await crew.NameOwnerAsync(command.Id, command.Seat, formerOwner, cancellationToken, command.ExpectedVersion);
        return Unit.Value;
    }
}
