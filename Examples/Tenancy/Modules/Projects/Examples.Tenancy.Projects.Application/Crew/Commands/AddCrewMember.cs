using DDDToolkit.Supporting.Membership.UseCases;
using Mediator;

namespace Examples.Tenancy.Projects.Application.Crew.Commands;

/// <summary>
/// Puts an active seat, the caller's own included, on a project's crew, from now until a moment or for good. With
/// a project role, the seat is given that role for the same period in the same change; without one it is on the
/// crew with no role, which lets it see the project and nothing more.
/// </summary>
/// <remarks>
/// Whoever manages the crew may put anyone on it, themselves included, in any project role of the tenant in use,
/// without holding that role's keys: on a crew a role gives only what acts on the project. That is this
/// application's rule, and the whole of it: the command requires <see cref="RequiredKey"/> on the project, and
/// nothing is checked for a caller who adds their own seat.
/// </remarks>
/// <param name="Project">The project.</param>
/// <param name="Seat">The seat.</param>
/// <param name="Role">A project role to give it at once, for the same period. <see langword="null"/> for none.</param>
/// <param name="Until">When the membership ends, or <see langword="null"/> for no end.</param>
/// <param name="ExpectedVersion">
/// The version of the project the caller last read, or <see langword="null"/> to change it as it is now. Any other
/// version than the project's is a lost race: somebody changed it since, so the caller reads again and decides again.
/// </param>
public sealed record AddCrewMember(ProjectId Project, SeatId Seat, ProjectRoleId? Role, DateTimeOffset? Until, long? ExpectedVersion = null) : ICommand, IProjectsRequest
{
    /// <summary>The key the caller holds on the project.</summary>
    public const string RequiredKey = ProjectKeys.ManageCrew;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.On(RequiredKey, Project, ExpectedVersion);
}

/// <summary>
/// Handles <see cref="AddCrewMember"/>: loads the project that was checked, has the seat and the role admitted,
/// adds the member, with the role when one was named, and saves.
/// </summary>
/// <param name="store">Where projects are loaded and saved.</param>
/// <param name="checkedProject">What the access check read of the project.</param>
/// <param name="admission">Whether the seat may go on a crew, and the role on a member: the projects' rules.</param>
/// <param name="answers">Tenancy's answers about the current caller, for who added the member.</param>
/// <param name="clock">What "now" is: when the membership starts.</param>
public sealed class AddCrewMemberHandler(
    IProjectStore store,
    Checked<MemberHold<ProjectId>> checkedProject,
    MemberAdmission<ProjectId, SeatId, ProjectRoleId> admission,
    SampleAnswers answers,
    TimeProvider clock)
    : ICommandHandler<AddCrewMember>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.not-found</c>, <c>projects.seat-not-active</c>, <c>projects.role-not-for-members</c>,
    /// <c>projects.already-on-crew</c>, <c>projects.closed</c>, and <c>tenancy.invalid-period</c> for an end that is
    /// not after now.
    /// </exception>
    /// <exception cref="Exceptions.ConcurrencyConflictException">The project was changed since the access check, or while this was saved.</exception>
    public async ValueTask<Unit> Handle(AddCrewMember command, CancellationToken cancellationToken)
    {
        var seen = checkedProject.TakeFor(command);
        var project = await store.LoadAsync(seen.Resource, seen.Version, cancellationToken)
            ?? throw ProjectRefusals.Of(ProjectRefusals.NotFound);

        await admission.RequireMemberAsync(command.Seat, cancellationToken);
        if (command.Role is { } asked)
        {
            await admission.RequireRoleAsync(asked, cancellationToken);
        }

        var now = clock.GetUtcNow();
        var period = CrewPeriod.Between(now, command.Until);
        var by = answers.RequireTenant().Seat;
        if (command.Role is { } given)
        {
            project.AddToCrew(command.Seat, given, period, now, by);
        }
        else
        {
            project.AddToCrew(command.Seat, period, now, by);
        }

        await store.SaveAsync(cancellationToken);
        return Unit.Value;
    }
}
