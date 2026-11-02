using Mediator;

namespace Examples.Tenancy.Projects.Application.Lifecycle.Commands;

/// <summary>
/// Hangs a project at another active unit. Moving it changes who reaches it through the organization, so the
/// caller needs to be allowed to open a project there as well as to edit this one.
/// </summary>
/// <remarks>
/// The command requires two permissions, each named on a marker:
/// <list type="bullet">
/// <item><c>[OnResource]</c> on <see cref="Id"/> requires <see cref="ProjectPermissionNames.Edit"/> on the project. This
/// is a case of the Membership package.</item>
/// <item><see cref="AtProjectUnitAttribute"/> on <see cref="UnitId"/> requires <see cref="ProjectPermissionNames.Open"/>
/// at the unit the project moves to. Opening a project at that unit requires the same permission. This is the module's
/// own case of a permission at a unit.</item>
/// </list>
/// The generator combines the two calls with <c>AccessRequirement.All</c>, in the order of the parameters. The access
/// check runs them before the handler. A refusal has the code of the first case that fails: <c>projects.not-found</c> or
/// <c>projects.not-permitted</c> for the project, and <c>projects.not-permitted</c> with the permission and the unit for
/// the destination. <c>[ExpectedVersion]</c> belongs to the project's marker, because it is the only <c>[OnResource]</c>
/// on the request. The access check reads the unit from <see cref="UnitId"/>, and the handler moves the project to that
/// unit.
/// </remarks>
/// <param name="Id">The project.</param>
/// <param name="UnitId">The unit to hang it at.</param>
/// <param name="ExpectedVersion">
/// The version of the project the caller last read, or <see langword="null"/> to change it as it is now. Any other
/// version than the project's is a lost race: somebody changed it since, so the caller reads again and decides again.
/// </param>
public sealed record MoveProjectToUnit(
    [OnResource(ProjectPermissionNames.Edit)] ProjectId Id,
    [AtProjectUnit(ProjectPermissionNames.Open)] OrganizationUnitId UnitId,
    [ExpectedVersion] long? ExpectedVersion = null) : ICommand, IProjectsRequest;

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
