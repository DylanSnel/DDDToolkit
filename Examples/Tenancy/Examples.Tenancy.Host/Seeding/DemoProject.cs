using Examples.Tenancy.Shared.Domain.ValueObjects;

namespace Examples.Tenancy.Host.Seeding;

/// <summary>A project the seeder opens, with its owner, and the rest of its crew.</summary>
/// <param name="Number">Its number.</param>
/// <param name="Name">Its name.</param>
/// <param name="Id">Its fixed id.</param>
/// <param name="Unit">The unit it hangs at.</param>
/// <param name="Owner">Who owns it, on the crew with the crew lead role.</param>
/// <param name="Crew">Everyone else on the crew.</param>
/// <param name="PlannedDays">
/// How many days before and after the day it is seeded its planned range runs, or <see langword="null"/> for a
/// project that is not planned. Counted from the day of seeding, so today is within it on a fresh database.
/// </param>
public sealed record DemoProject(
    string Number,
    string Name,
    ProjectId Id,
    OrganizationUnitId Unit,
    DemoPerson Owner,
    IReadOnlyList<DemoCrewMember> Crew,
    (int Before, int After)? PlannedDays = null)
{
    /// <summary>The project's planned range when it is seeded on <paramref name="today"/>, or <see langword="null"/>.</summary>
    public DateRange? PlannedOn(DateOnly today)
        => PlannedDays is { } days ? new DateRange(today.AddDays(-days.Before), today.AddDays(days.After)) : null;
}
