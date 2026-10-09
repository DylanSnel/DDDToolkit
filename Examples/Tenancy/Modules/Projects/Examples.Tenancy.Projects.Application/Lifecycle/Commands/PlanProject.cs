using Mediator;

namespace Examples.Tenancy.Projects.Application.Lifecycle.Commands;

/// <summary>Plans a project for a range of days, or takes its planned range away.</summary>
/// <remarks>
/// Editing a project, as renaming it is, so it asks the same key. The range says which days the project takes
/// from now on: Inspections asks Projects' gate for it when an inspection is recorded, and what was recorded
/// before a change stays as it was.
/// </remarks>
/// <param name="Id">The project.</param>
/// <param name="Planned">The days the work is planned for, or <see langword="null"/> for none.</param>
/// <param name="ExpectedVersion">
/// The version of the project the caller last read, or <see langword="null"/> to change it as it is now. Any other
/// version than the project's is a lost race: somebody changed it since, so the caller reads again and decides again.
/// </param>
public sealed record PlanProject(ProjectId Id, DateRange? Planned, long? ExpectedVersion = null) : ICommand, IProjectsRequest
{
    /// <summary>The key the caller holds on the project.</summary>
    public const string RequiredKey = ProjectKeys.Edit;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.On(RequiredKey, Id, ExpectedVersion);
}

/// <summary>Handles <see cref="PlanProject"/>: loads the project its command names, plans it and saves.</summary>
/// <param name="store">Where projects are loaded and saved.</param>
public sealed class PlanProjectHandler(IProjectStore store) : ICommandHandler<PlanProject>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException"><c>projects.not-found</c>, <c>projects.closed</c>, <c>projects.planned-range-invalid</c>.</exception>
    /// <exception cref="Exceptions.ConcurrencyConflictException">The project is at another version than the caller named, or was changed while this was saved.</exception>
    public async ValueTask<Unit> Handle(PlanProject command, CancellationToken cancellationToken)
    {
        var project = await store.LoadAsync(command.Id, command.ExpectedVersion, cancellationToken)
            ?? throw ProjectRefusals.Refuse(ProjectRefusals.NotFound);

        project.Plan(command.Planned);
        await store.SaveAsync(cancellationToken);
        return Unit.Value;
    }
}
