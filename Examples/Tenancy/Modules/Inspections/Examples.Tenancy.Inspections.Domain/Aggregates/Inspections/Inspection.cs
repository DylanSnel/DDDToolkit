using DDDToolkit.Abstractions.Attributes;
using Examples.Tenancy.Inspections.Domain.Aggregates.Inspections.Events;
using Examples.Tenancy.Inspections.Domain.Aggregates.Inspections.ValueObjects;

namespace Examples.Tenancy.Inspections.Domain.Aggregates.Inspections;

/// <summary>
/// An inspection: what a seat found on a project, recorded once. The aggregate root of this module.
/// </summary>
/// <remarks>
/// An inspection is a record of something that happened, so it has no methods that change it: recording it is
/// constructing it. Who may record one on a project is not the inspection's business, nor this module's to
/// decide on its own. Projects answers it, through the crew and through the organization, and the command that
/// records one, <c>RecordInspection</c>, is checked against that answer before its handler makes the inspection;
/// the inspection only keeps what it needs itself: a title that is not blank and not too long, and the days it
/// covers, a range whose last day is not before its first.
/// <para>
/// One rule crosses the modules. A project may be planned for a range of days, and an inspection's days then lie
/// within it. The plan is Projects' to know, so the command passes in what Projects' gate answered and the
/// inspection refuses what that rules out, under a code of its own. It keeps no copy of the plan: a project
/// planned differently later leaves the inspections it has as they are.
/// </para>
/// <para>
/// The constructor refuses with a coded <see cref="Exceptions.RefusalException"/> before anything is made. The
/// nested invariants, a file each in <c>Invariants/</c>, state the title's rule and the days' own after the fact,
/// with the same codes, as the net under code that went round the constructor; the save runs them.
/// </para>
/// </remarks>
[AggregateRoot<InspectionId>]
public sealed partial class Inspection
{
    /// <summary>The longest title.</summary>
    public const int LongestTitle = 200;

    /// <summary>
    /// Records an inspection of <paramref name="projectId"/>, made by <paramref name="recordedBy"/> at
    /// <paramref name="at"/>.
    /// </summary>
    /// <param name="id">Its id.</param>
    /// <param name="tenantId">The tenant it belongs to, the project's, for good.</param>
    /// <param name="projectId">The project it is about.</param>
    /// <param name="title">What was found: trimmed, 1 to <see cref="LongestTitle"/> characters.</param>
    /// <param name="days">The days it covers.</param>
    /// <param name="planned">
    /// The days the project is planned for, as Projects answered the command, or <see langword="null"/> for a
    /// project with no planned range, which takes any days.
    /// </param>
    /// <param name="recordedBy">The seat that recorded it.</param>
    /// <param name="at">When it was recorded.</param>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>inspections.title-invalid</c>, <c>inspections.days-invalid</c>, <c>inspections.outside-planned-range</c>.
    /// </exception>
    public Inspection(
        InspectionId id,
        TenantId tenantId,
        ProjectId projectId,
        string title,
        DateRange days,
        DateRange? planned,
        SeatId recordedBy,
        DateTimeOffset at)
        : base(id)
    {
        Title = CheckedTitle(title);
        Days = CheckedDays(days, planned);
        TenantId = tenantId;
        ProjectId = projectId;
        RecordedBy = recordedBy;
        RecordedAt = at;

        RaiseDomainEvent(new InspectionRecorded(id, tenantId, projectId, recordedBy));
    }

    /// <summary>The tenant the inspection belongs to. It never changes; the tenant filter reads it.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>The project it is about.</summary>
    public ProjectId ProjectId { get; private set; }

    /// <summary>What was found.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>The days it covers: what was inspected happened, or was seen, on these days.</summary>
    public DateRange Days { get; private set; } = null!;

    /// <summary>The seat that recorded it.</summary>
    public SeatId RecordedBy { get; private set; }

    /// <summary>When it was recorded.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    private static string CheckedTitle(string title)
        => title?.Trim() is { Length: > 0 and <= LongestTitle } trimmed
            ? trimmed
            : throw InspectionRefusals.Of(InspectionRefusals.TitleInvalid, ("Max", LongestTitle));

    private static DateRange CheckedDays(DateRange days, DateRange? planned)
    {
        if (days is not { IsValid: true })
        {
            throw InspectionRefusals.Of(InspectionRefusals.DaysInvalid);
        }

        // The planned range is said as two ISO dates: an argument is a value a translation can place.
        return planned is null || planned.Contains(days)
            ? days
            : throw InspectionRefusals.Of(
                InspectionRefusals.OutsidePlannedRange,
                ("From", planned.From.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
                ("Until", planned.Until.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
    }
}
