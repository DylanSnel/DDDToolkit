using Mediator;

namespace Examples.Tenancy.Projects.Application.Lifecycle.Commands;

/// <summary>
/// Hangs a project at another active unit. Moving it changes who reaches it through the organization, so the
/// caller needs to be allowed to open a project there as well as to edit this one.
/// </summary>
/// <remarks>
/// Two keys in two places. <see cref="RequiredKey"/> on the project is what the command declares, and is checked
/// before the handler. <see cref="DestinationKey"/> at the unit it moves to is asked by the handler: a requirement
/// names one key on one thing, and this second one is the command's own rule.
/// </remarks>
/// <param name="Id">The project.</param>
/// <param name="UnitId">The unit to hang it at.</param>
/// <param name="ExpectedVersion">
/// The version of the project the caller last read, or <see langword="null"/> to change it as it is now. Any other
/// version than the project's is a lost race: somebody changed it since, so the caller reads again and decides again.
/// </param>
public sealed record MoveProjectToUnit(ProjectId Id, OrganizationUnitId UnitId, long? ExpectedVersion = null) : ICommand, IProjectsRequest
{
    /// <summary>The key the caller holds on the project.</summary>
    public const string RequiredKey = ProjectKeys.Edit;

    /// <summary>The key the caller holds at the unit the project moves to: the one opening a project there needs.</summary>
    public const string DestinationKey = OpenProject.RequiredKey;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.On(RequiredKey, Id, ExpectedVersion);
}

/// <summary>
/// Handles <see cref="MoveProjectToUnit"/>: loads the project its command names, checks the destination, moves it
/// and saves.
/// </summary>
/// <param name="store">Where projects are loaded and saved.</param>
/// <param name="reads">Where Tenancy's rows are read: one reading for everything this command asks.</param>
/// <param name="access">Who holds which key where, for the destination.</param>
/// <param name="tenancy">What the tenant allows: an active unit.</param>
public sealed class MoveProjectToUnitHandler(
    IProjectStore store,
    IProjectReads reads,
    ProjectAccess access,
    ProjectTenancy tenancy)
    : ICommandHandler<MoveProjectToUnit>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.not-found</c>; <c>projects.not-permitted</c> without <see cref="MoveProjectToUnit.DestinationKey"/>
    /// at the unit; <c>projects.unit-not-active</c>, <c>projects.closed</c>.
    /// </exception>
    /// <exception cref="Exceptions.ConcurrencyConflictException">The project is at another version than the caller named, or was changed while this was saved.</exception>
    public async ValueTask<Unit> Handle(MoveProjectToUnit command, CancellationToken cancellationToken)
    {
        var project = await store.LoadAsync(command.Id, command.ExpectedVersion, cancellationToken)
            ?? throw ProjectRefusals.Of(ProjectRefusals.NotFound);

        // The second key, at the destination, and only then whether the destination takes anything new: both
        // asked on one reading, of this command's own.
        await using var reading = reads.Open();
        await access.RequireAtAsync(reading, command.UnitId, MoveProjectToUnit.DestinationKey, cancellationToken);
        await tenancy.RequireActiveUnitAsync(reading, command.UnitId, cancellationToken);

        project.MoveToUnit(command.UnitId);
        await store.SaveAsync(cancellationToken);
        return Unit.Value;
    }
}
