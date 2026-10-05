using DDDToolkit.Supporting.Membership.UseCases;
using Mediator;

namespace Examples.Tenancy.Projects.Application.Ownership.Commands;

/// <summary>
/// Names another owner, an active seat, who goes on the crew, when not on it yet, and holds the tenant's crew
/// lead role there with no end. The old owner loses that role and nothing else: the seat stays on the crew, with
/// any other role it holds there.
/// </summary>
/// <remarks>
/// Only the organization names an owner: <see cref="RequiredKey"/> is held at the project's unit or above it,
/// never through the crew, whatever role is on it, and not by owning the project either. So the crew's lead cannot
/// hand the project on. That is this application's rule; the projects' rules name the key as the one that changes
/// the owner, so the database holds a caller to it as well.
/// </remarks>
/// <param name="Id">The project.</param>
/// <param name="Seat">The seat that is to own it.</param>
/// <param name="ExpectedVersion">
/// The version of the project the caller last read, or <see langword="null"/> to change it as it is now. Any other
/// version than the project's is a lost race: somebody changed it since, so the caller reads again and decides again.
/// </param>
public sealed record ChangeProjectOwner(ProjectId Id, SeatId Seat, long? ExpectedVersion = null) : ICommand, IProjectsRequest
{
    /// <summary>The key the caller holds on the project, through the organization.</summary>
    public const string RequiredKey = ProjectKeys.ChangeOwner;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.On(RequiredKey, Id, ExpectedVersion);
}

/// <summary>
/// Handles <see cref="ChangeProjectOwner"/>: loads the project its command names, has the seat admitted and finds
/// the crew lead's role, names the owner and saves.
/// </summary>
/// <param name="store">Where projects are loaded and saved.</param>
/// <param name="admission">Whether the seat may go on a crew, and which project role is the crew lead's.</param>
/// <param name="clock">What "now" is: when the new owner's membership starts to count.</param>
public sealed class ChangeProjectOwnerHandler(
    IProjectStore store,
    MemberAdmission<ProjectId, SeatId, ProjectRoleId> admission,
    TimeProvider clock)
    : ICommandHandler<ChangeProjectOwner>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.not-found</c>, <c>projects.seat-not-active</c>, <c>projects.no-lead-role</c>,
    /// <c>projects.already-owner</c>, <c>projects.closed</c>.
    /// </exception>
    /// <exception cref="Exceptions.ConcurrencyConflictException">The project is at another version than the caller named, or was changed while this was saved.</exception>
    public async ValueTask<Unit> Handle(ChangeProjectOwner command, CancellationToken cancellationToken)
    {
        var project = await store.LoadAsync(command.Id, command.ExpectedVersion, cancellationToken)
            ?? throw ProjectRefusals.Of(ProjectRefusals.NotFound);

        await admission.RequireMemberAsync(command.Seat, cancellationToken);
        var leadRole = await admission.OwnerRoleAsync(cancellationToken);

        project.ChangeOwner(command.Seat, leadRole, clock.GetUtcNow());
        await store.SaveAsync(cancellationToken);
        return Unit.Value;
    }
}
