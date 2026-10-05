using DDDToolkit.Supporting.Membership.UseCases;
using Mediator;

namespace Examples.Tenancy.Projects.Application.Lifecycle.Commands;

/// <summary>
/// Opens a project at an active unit, with its owner on the crew, holding the tenant's crew lead role there: the
/// project role made from the crew lead's starter role.
/// </summary>
/// <remarks>
/// Asked at the unit, since there is no project yet: the caller holds <see cref="RequiredKey"/> there or above
/// it. The owner is the caller unless the command names somebody else, and naming somebody else is the
/// organization's decision, so it needs <see cref="OwnerKey"/> at the unit too. That second key depends on who is
/// named, so it is not part of what the command declares: the handler asks for it, in plain sight. The unit the
/// project is opened at is the one the command names, which is the unit its check asked about; and the database,
/// which checks every row, lets a seat open a project only at a unit where it holds <see cref="RequiredKey"/>,
/// however the handler was reached.
/// </remarks>
/// <param name="Number">Its number, unique in the tenant.</param>
/// <param name="Name">Its name.</param>
/// <param name="UnitId">The unit it hangs at.</param>
/// <param name="Owner">
/// The seat that owns it: the caller when left out. Naming somebody else needs <see cref="OwnerKey"/> at the unit;
/// system work always names one.
/// </param>
/// <param name="Id">
/// Its id, for system work that seeds or imports projects whose ids are known beforehand; a new one otherwise. No
/// route has a field for it, and a seat that sends one is a mistake in the calling code.
/// </param>
/// <param name="Planned">
/// The days the work is planned for, or <see langword="null"/> to plan it later (<see cref="PlanProject"/>).
/// </param>
public sealed record OpenProject(string Number, string Name, OrganizationUnitId UnitId, SeatId? Owner = null, ProjectId? Id = null, DateRange? Planned = null)
    : ICommand<ProjectId>, IProjectsRequest
{
    /// <summary>The key the caller holds at the unit.</summary>
    public const string RequiredKey = ProjectKeys.Open;

    /// <summary>The key the caller also holds at the unit to name somebody else as the owner.</summary>
    public const string OwnerKey = ProjectKeys.ChangeOwner;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new ProjectsRequirement.AtUnit(RequiredKey, UnitId);
}

/// <summary>Handles <see cref="OpenProject"/>: decides everything first, then opens the project and saves once.</summary>
/// <param name="store">Where projects are added and saved.</param>
/// <param name="reads">Where Tenancy's rows are read: one reading for everything this command asks.</param>
/// <param name="access">Who holds which key where, for the owner rule.</param>
/// <param name="tenancy">What the tenant allows: an active unit.</param>
/// <param name="admission">Whether the owner may go on a crew, and which project role is the crew lead's.</param>
/// <param name="answers">Tenancy's answers about the current caller.</param>
/// <param name="clock">What "now" is.</param>
public sealed class OpenProjectHandler(
    IProjectStore store,
    IProjectReads reads,
    ProjectAccess access,
    ProjectTenancy tenancy,
    MemberAdmission<ProjectId, SeatId, ProjectRoleId> admission,
    SampleAnswers answers,
    TimeProvider clock)
    : ICommandHandler<OpenProject, ProjectId>
{
    /// <inheritdoc />
    /// <returns>The new project's id.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.not-permitted</c> without <see cref="OpenProject.OwnerKey"/> at the unit to name somebody else;
    /// <c>projects.unit-not-active</c>, <c>projects.seat-not-active</c> for the owner, <c>projects.no-lead-role</c>,
    /// <c>projects.number-taken</c>, <c>projects.number-invalid</c>, <c>projects.name-invalid</c>,
    /// <c>projects.planned-range-invalid</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">A seat chose the project's id; or system work named no owner.</exception>
    public async ValueTask<ProjectId> Handle(OpenProject command, CancellationToken cancellationToken)
    {
        // The unit the command names, which is the one its access check asked about.
        var unit = command.UnitId;
        var scope = answers.RequireTenant();

        // The id is nobody's to choose through a client: only work the application does itself knows one beforehand.
        if (command.Id is not null && !scope.BySystem)
        {
            throw new InvalidOperationException("Only system work chooses a project's id: OpenProject.Id is for seeding and imports, and a seat's project gets a new one.");
        }

        var owner = command.Owner
            ?? (scope.BySystem
                ? throw new InvalidOperationException("System work opens a project for an owner it names: OpenProject.Owner is required.")
                : scope.Seat!.Value);

        // What is asked of the organization's keys and units is asked on one reading, of this command's own.
        await using var reading = reads.Open();

        // The owner rule: opening a project for oneself needs the one key; for somebody else, the organization's too.
        if (owner != scope.Seat)
        {
            await access.RequireAtAsync(reading, unit, OpenProject.OwnerKey, cancellationToken);
        }

        await tenancy.RequireActiveUnitAsync(reading, unit, cancellationToken);

        // Whether the owner may be on a crew, and the role every owner holds: the projects' rules answer both, on
        // the request's context, where the role is a row of this module's.
        await admission.RequireMemberAsync(owner, cancellationToken);
        var leadRole = await admission.OwnerRoleAsync(cancellationToken);

        var number = command.Number?.Trim() ?? string.Empty;
        if (await store.NumberTakenAsync(number, cancellationToken))
        {
            throw ProjectRefusals.Of(ProjectRefusals.NumberTaken, ("Number", number));
        }

        var opened = new Project(command.Id ?? ProjectId.CreateSequential(), scope.Tenant, number, command.Name, unit, owner, leadRole, clock.GetUtcNow(), command.Planned);
        store.Add(opened);
        await store.SaveAsync(cancellationToken);
        return opened.Id;
    }
}
