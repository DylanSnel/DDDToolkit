using DDDToolkit.Supporting.Membership.UseCases;
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
/// Handles <see cref="GiveCrewRole"/>: loads the project its command names, has the role admitted, gives it and
/// saves.
/// </summary>
/// <param name="store">Where projects are loaded and saved.</param>
/// <param name="admission">Whether the role may go on a member: one of the tenant's project roles in use.</param>
/// <param name="tenancy">Tenancy, asked about the current caller, for who gave the role.</param>
/// <param name="clock">What "now" is: when the role starts.</param>
public sealed class GiveCrewRoleHandler(
    IProjectStore store,
    MemberAdmission<ProjectId, SeatId, ProjectRoleId> admission,
    TenancyAccessManager tenancy,
    TimeProvider clock)
    : ICommandHandler<GiveCrewRole>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.not-found</c>, <c>projects.role-not-for-members</c>, <c>projects.member-not-found</c>,
    /// <c>projects.crew-role-held</c>, <c>projects.closed</c>, and <c>tenancy.invalid-period</c> for an end that is
    /// not after now.
    /// </exception>
    /// <exception cref="Exceptions.ConcurrencyConflictException">The project is at another version than the caller named, or was changed while this was saved.</exception>
    public async ValueTask<Unit> Handle(GiveCrewRole command, CancellationToken cancellationToken)
    {
        var project = await store.LoadAsync(command.Project, command.ExpectedVersion, cancellationToken)
            ?? throw ProjectRefusals.Refuse(ProjectRefusals.NotFound);

        await admission.RequireRoleAsync(command.Role, cancellationToken);

        var now = clock.GetUtcNow();
        project.GiveCrewRole(command.Seat, command.Role, CrewPeriod.Between(now, command.Until), now, tenancy.CurrentTenant.Require().Seat);
        await store.SaveAsync(cancellationToken);
        return Unit.Value;
    }
}
