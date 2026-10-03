using Mediator;

namespace Examples.Tenancy.Inspections.Application.Recording.Commands;

/// <summary>
/// Records an inspection on a project, by the caller's seat, or by the seat system work in the tenant acts for.
/// </summary>
/// <remarks>
/// The caller holds <see cref="RequiredKey"/> on the project, and the project is open: Projects' gate answers
/// both before the handler runs. The project comes first and the title and the days after, so a caller who may
/// not record learns nothing from a bad title, nor from days outside the project's planned range.
/// </remarks>
/// <param name="Project">The project.</param>
/// <param name="Title">What was found.</param>
/// <param name="Days">
/// The days the inspection covers; left out, the day it is recorded, in UTC. That is the clock's day and not
/// necessarily the caller's: early in the morning east of UTC it is still yesterday. A client that knows its
/// user's day sends it, and the refusal for days outside a project's plan says what leaving them out means.
/// </param>
public sealed record RecordInspection(ProjectId Project, string Title, DateRange? Days = null) : ICommand<InspectionId>, IInspectionsRequest
{
    /// <summary>The key the caller holds on the project.</summary>
    public const string RequiredKey = InspectionKeys.Record;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new InspectionsRequirement.OnOpenProject(RequiredKey, Project, BySeat: true);
}

/// <summary>
/// Handles <see cref="RecordInspection"/>: makes the inspection, which checks its own title and days, adds it and
/// saves.
/// </summary>
/// <remarks>
/// Recording is constructing: there is nothing to load. The project is the one the access check asked Projects'
/// gate about; the tenant and the recording seat come from Tenancy's answers about the caller, never from the
/// request, and the save check keeps the write to that tenant.
/// <para>
/// The days are held to the project's planned range, which is Projects' to know. The gate's answer carried it, the
/// access check kept it with the project, and the inspection refuses days outside it under Inspections' own code.
/// This module reads no table of Projects' for it and repeats no rule of Projects': it compares two ranges of a
/// type both modules share.
/// </para>
/// <para>
/// The inspection is recorded on the project as it was when the gate answered. Projects ties a command's check
/// to its save through the project's version; this module holds no version of another module's project, so a
/// project closed, or a crew membership ended, between the gate's answer and this save still gets the
/// inspection. It is one more record on a project the caller could record on a moment before, and changes
/// nothing of the project itself.
/// </para>
/// </remarks>
/// <param name="store">Where inspections are added and saved.</param>
/// <param name="gated">The project this command passed the gate for.</param>
/// <param name="answers">Tenancy's answers about the current caller.</param>
/// <param name="clock">What "now" is.</param>
public sealed class RecordInspectionHandler(IInspectionStore store, Checked<GatedProject> gated, SampleAnswers answers, TimeProvider clock)
    : ICommandHandler<RecordInspection, InspectionId>
{
    /// <inheritdoc />
    /// <returns>The new inspection's id.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>inspections.title-invalid</c>, <c>inspections.days-invalid</c>, <c>inspections.outside-planned-range</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The command did not pass the access check: no project passed the gate for it.</exception>
    public async ValueTask<InspectionId> Handle(RecordInspection command, CancellationToken cancellationToken)
    {
        var (project, planned) = gated.TakeFor(command);
        var scope = answers.RequireTenant();
        var now = clock.GetUtcNow();

        var inspection = new Inspection(
            InspectionId.CreateSequential(),
            scope.Tenant,
            project,
            command.Title,
            command.Days ?? DateRange.Of(DateOnly.FromDateTime(now.UtcDateTime)),
            planned,
            ActingSeat.Of(scope),
            now);

        store.Add(inspection);
        await store.SaveAsync(cancellationToken);
        return inspection.Id;
    }
}
