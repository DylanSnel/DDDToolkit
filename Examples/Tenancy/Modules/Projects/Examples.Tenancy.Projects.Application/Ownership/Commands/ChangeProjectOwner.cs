using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Membership.UseCases;
using Mediator;

namespace Examples.Tenancy.Projects.Application.Ownership.Commands;

/// <summary>
/// Names another owner, an active seat, who goes on the crew with no end, when not on it yet, and holds no project
/// role there: the owner holds what an owner does by owning the project, so the roles it held on the crew go. The
/// old owner becomes what the command says: a crew member in <see cref="FormerOwnerRole"/>, or, when it names none,
/// nobody on the crew.
/// </summary>
/// <remarks>
/// Only the organization names an owner: <see cref="RequiredKey"/> is held at the project's unit or above it,
/// never through the crew, whatever role is on it, and not by owning the project either
/// (<see cref="Access.ProjectPermissions.ChangeOwner"/>). So the owner cannot hand the project on. The projects'
/// rules name the key as the one that locks the owner, so the database holds a caller to it as well.
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
[RequiresKey(RequiredKey)]
public sealed record ChangeProjectOwner([OnResource] ProjectId Id, SeatId Seat, ProjectRoleId? FormerOwnerRole, [ExpectedVersion] long? ExpectedVersion = null) : ICommand, IProjectsRequest
{
    /// <summary>The key the caller holds on the project, through the organization.</summary>
    public const string RequiredKey = ProjectKeys.ChangeOwner;
}

/// <summary>
/// Handles <see cref="ChangeProjectOwner"/>: loads the project its command names, has the seat admitted, and the
/// role the old owner stays in, names the owner and saves.
/// </summary>
/// <param name="store">Where projects are loaded and saved.</param>
/// <param name="admission">Whether the seat may go on a crew, and whether the old owner's role is one of the tenant's in use.</param>
/// <param name="clock">What "now" is: when the new owner's membership starts to count.</param>
public sealed class ChangeProjectOwnerHandler(
    IProjectStore store,
    MemberAdmission<ProjectId, SeatId, ProjectRoleId> admission,
    TimeProvider clock)
    : ICommandHandler<ChangeProjectOwner>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.not-found</c>, <c>projects.seat-not-active</c>, <c>projects.role-not-for-members</c> for the old
    /// owner's role, <c>projects.already-owner</c>, <c>projects.closed</c>.
    /// </exception>
    /// <exception cref="Exceptions.ConcurrencyConflictException">The project is at another version than the caller named, or was changed while this was saved.</exception>
    public async ValueTask<Unit> Handle(ChangeProjectOwner command, CancellationToken cancellationToken)
    {
        var project = await store.LoadAsync(command.Id, command.ExpectedVersion, cancellationToken)
            ?? throw ProjectRefusals.Refuse(ProjectRefusals.NotFound);

        await admission.RequireMemberAsync(command.Seat, cancellationToken);
        FormerOwner<ProjectRoleId> formerOwner = FormerOwner.Leaves;
        if (command.FormerOwnerRole is { } role)
        {
            await admission.RequireRoleAsync(role, cancellationToken);
            formerOwner = FormerOwner.StaysAs(role);
        }

        project.ChangeOwner(command.Seat, formerOwner, clock.GetUtcNow());
        await store.SaveAsync(cancellationToken);
        return Unit.Value;
    }
}
