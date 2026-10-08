using DDDToolkit.Supporting.Membership.UseCases;
using Mediator;

namespace Examples.Tenancy.Projects.Application.Crew.Commands;

/// <summary>
/// Takes a project role from a crew member, the caller included, who stays on the crew with whatever else it
/// holds there. The owner keeps the crew lead's role until somebody else is named owner, whoever asks, the owner
/// included.
/// </summary>
/// <param name="Project">The project.</param>
/// <param name="Seat">The seat on its crew.</param>
/// <param name="Role">The project role to take.</param>
/// <param name="ExpectedVersion">
/// The version of the project the caller last read, or <see langword="null"/> to change it as it is now. Any other
/// version than the project's is a lost race: somebody changed it since, so the caller reads again and decides again.
/// </param>
public sealed record TakeCrewRole(ProjectId Project, SeatId Seat, ProjectRoleId Role, long? ExpectedVersion = null) : ICommand, IProjectsRequest
{
    /// <summary>The key the caller holds on the project.</summary>
    public const string RequiredKey = ProjectKeys.ManageCrew;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.On(RequiredKey, Project, ExpectedVersion);
}

/// <summary>
/// Handles <see cref="TakeCrewRole"/>: loads the project its command names, asks which role is the crew lead's when
/// the seat is the owner's, takes the role and saves.
/// </summary>
/// <remarks>
/// Any role can be taken, one the tenant has since archived too: what was given once can always be taken back.
/// Which role an owner may not lose is the tenant's project role made from the crew lead's starter role, and is
/// asked only for the owner; the project refuses what the answer rules out.
/// </remarks>
/// <param name="store">Where projects are loaded and saved.</param>
/// <param name="admission">Which of the tenant's project roles is the crew lead's.</param>
/// <param name="answers">Tenancy's answers about the current caller: whether the seat is the caller's own.</param>
public sealed class TakeCrewRoleHandler(
    IProjectStore store,
    MemberAdmission<ProjectId, SeatId, ProjectRoleId> admission,
    SampleAnswers answers)
    : ICommandHandler<TakeCrewRole>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.not-found</c>, <c>projects.member-not-found</c>, <c>projects.crew-role-not-found</c>,
    /// <c>projects.owner-protected</c>, <c>projects.closed</c>.
    /// </exception>
    /// <exception cref="Exceptions.ConcurrencyConflictException">The project is at another version than the caller named, or was changed while this was saved.</exception>
    public async ValueTask<Unit> Handle(TakeCrewRole command, CancellationToken cancellationToken)
    {
        var project = await store.LoadAsync(command.Project, command.ExpectedVersion, cancellationToken)
            ?? throw ProjectRefusals.Refuse(ProjectRefusals.NotFound);

        var leadRole = command.Seat == project.OwnerSeatId ? await admission.FindOwnerRoleAsync(cancellationToken) : null;
        project.TakeCrewRole(command.Seat, command.Role, leadRole);

        // A role of the caller's own may be the one this very change was allowed by, once this command's check
        // let it through: see OwnPlaceOnTheCrew. Either way it writes this project and nothing an earlier command
        // may have left in the unit of work.
        using (OwnPlaceOnTheCrew.BeginSave(command, command.Seat, answers.RequireTenant()))
        {
            await store.SaveOnlyAsync(project, cancellationToken);
        }

        return Unit.Value;
    }
}
