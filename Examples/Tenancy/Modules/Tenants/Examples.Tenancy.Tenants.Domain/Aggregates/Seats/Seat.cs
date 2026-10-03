namespace Examples.Tenancy.Tenants.Domain.Aggregates.Seats;

/// <summary>
/// The application's seat, one person's place in one tenant: the package's, with a job title and a rule
/// about it.
/// </summary>
/// <remarks>
/// The seat keeps no e-mail address and no contact details: the package knows a person only by the verified
/// identity their token carries. An application that wants more about a person adds it here, as the job title
/// is added. Placements and grants stay the package's; they are changed through its use cases only.
/// </remarks>
[SeatAggregate<SeatId>]
public sealed partial class Seat
{
    /// <summary>The longest job title.</summary>
    public const int MaxJobTitleLength = 80;

    /// <summary>What the person does, such as "Site surveyor", or <see langword="null"/>.</summary>
    public string? JobTitle { get; private set; }

    /// <summary>Sets or clears the job title. <see cref="JobTitleLength"/> judges it when the seat is saved.</summary>
    public void ChangeJobTitle(string? jobTitle) => JobTitle = jobTitle;
}
