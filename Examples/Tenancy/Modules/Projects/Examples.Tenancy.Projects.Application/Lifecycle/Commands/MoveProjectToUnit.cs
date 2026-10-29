using Mediator;

namespace Examples.Tenancy.Projects.Application.Lifecycle.Commands;

/// <summary>
/// Hangs a project at another active unit. Moving it changes who reaches it through the organization, so the
/// caller needs to be allowed to open a project there as well as to edit this one.
/// </summary>
/// <remarks>
/// Two permissions in two places, and both hold: <see cref="RequiredPermission"/> on the project, a case of the Membership
/// package's, and <see cref="DestinationPermission"/> at the unit it moves to, the module's own case of a permission at a unit,
/// combined with <c>AccessRequirement.All</c> and checked at the door before the handler, in that order. A refusal is
/// the first failing case's own, <c>projects.not-found</c> or <c>projects.not-permitted</c> for the project and
/// <c>projects.not-permitted</c> with the permission and the unit for the destination. No attribute says two permissions, so the
/// request answers the property itself. The unit the permission is checked at is the one the command names, so the handler
/// moves the project to <see cref="UnitId"/> and to no other.
/// </remarks>
/// <param name="Id">The project.</param>
/// <param name="UnitId">The unit to hang it at.</param>
/// <param name="ExpectedVersion">
/// The version of the project the caller last read, or <see langword="null"/> to change it as it is now. Any other
/// version than the project's is a lost race: somebody changed it since, so the caller reads again and decides again.
/// </param>
public sealed record MoveProjectToUnit(ProjectId Id, OrganizationUnitId UnitId, long? ExpectedVersion = null) : ICommand, IProjectsRequest
{
    /// <summary>The permission the caller holds on the project.</summary>
    public const string RequiredPermission = ProjectPermissionNames.Edit;

    /// <summary>The permission the caller holds at the unit the project moves to: the one opening a project there needs.</summary>
    public const string DestinationPermission = OpenProject.RequiredPermission;

    /// <summary>Both permissions, the project's first: the one on the project, through its crew or the organization, and the one at the destination.</summary>
    AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.All(
        MemberAccess.On(RequiredPermission, Id, ExpectedVersion),
        new ProjectsRequirement.AtUnit(DestinationPermission, UnitId));
}

/// <summary>
/// Handles <see cref="MoveProjectToUnit"/>: loads the project its command names, checks that the destination takes
/// anything new, moves it and saves.
/// </summary>
/// <param name="store">Where projects are loaded and saved.</param>
/// <param name="reads">Where Tenancy's rows are read, for the destination.</param>
/// <param name="organization">What the tenant allows: an active unit.</param>
public sealed class MoveProjectToUnitHandler(
    IProjectStore store,
    IProjectReads reads,
    ProjectTenancy organization)
    : ICommandHandler<MoveProjectToUnit>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.not-found</c>; <c>projects.unit-not-active</c>, <c>projects.closed</c>.
    /// </exception>
    /// <exception cref="Exceptions.ConcurrencyConflictException">The project is at another version than the caller named, or was changed while this was saved.</exception>
    public async ValueTask<Unit> Handle(MoveProjectToUnit command, CancellationToken cancellationToken)
    {
        var project = await store.LoadAsync(command.Id, command.ExpectedVersion, cancellationToken)
            ?? throw ProjectRefusals.Refuse(ProjectRefusals.NotFound);

        // The permissions were checked at the door, at the unit this command names: whether that unit takes anything new is
        // the tenant's state, not the caller's right, so it is asked here.
        await using var reading = reads.Open();
        await organization.RequireActiveUnitAsync(reading, command.UnitId, cancellationToken);

        project.MoveToUnit(command.UnitId);
        await store.SaveAsync(cancellationToken);
        return Unit.Value;
    }
}
