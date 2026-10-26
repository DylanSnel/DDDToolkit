using DDDToolkit.Invariants;
using DDDToolkit.Supporting.Tenancy.TestHost.Contracts;

namespace DDDToolkit.Supporting.Tenancy.TestHost.Domain;

/// <summary>
/// The application's seat: the package's, with the name it is shown by in its tenant, a job title and a rule about
/// the job title, and whether it is an employee's or an external person's, which a restrictive row access rule of
/// the application's may read.
/// </summary>
/// <remarks>
/// The name is this application's own, as an application's would be: Tenancy keeps none and reads none. Whoever makes
/// a seat sets it in the callback of the use case that makes it, and the directory answers this class whole, from
/// which whoever shows a seat selects it.
/// </remarks>
[SeatAggregate<SeatId>]
public sealed partial class HostSeat
{
    /// <summary>The longest job title.</summary>
    public const int MaxJobTitleLength = 80;

    /// <summary>The name the seat is shown by in its tenant, or <see langword="null"/> when nobody gave it one.</summary>
    public string? DisplayName { get; private set; }

    /// <summary>What the person does, or <see langword="null"/>.</summary>
    public string? JobTitle { get; private set; }

    /// <summary>Whether the seat is an employee's, as every seat is until the application says otherwise.</summary>
    public HostSeatKind Kind { get; private set; }

    /// <summary>Marks the seat as an external person's, one who is no employee.</summary>
    public void MarkExternal() => Kind = HostSeatKind.External;

    /// <summary>Gives the seat the name it is shown by, or takes it away.</summary>
    public void Rename(string? displayName) => DisplayName = displayName;

    /// <summary>Sets or clears the job title.</summary>
    public void ChangeJobTitle(string? jobTitle) => JobTitle = jobTitle;

    /// <summary>
    /// Welcomes the person to the seat with an event of the application's own, <see cref="HostSeatWelcomed"/>: what a
    /// callback of the package's raises goes out with the save that makes the seat.
    /// </summary>
    public void Welcome() => RaiseDomainEvent(new HostSeatWelcomed(Id));

    /// <summary>A job title is at most <see cref="MaxJobTitleLength"/> characters.</summary>
    public sealed class JobTitleLength : IInvariant<HostSeat>
    {
        /// <inheritdoc />
        public string Code => "host.seat.job-title";

        /// <inheritdoc />
        public InvariantFailure? Check(HostSeat entity)
            => entity.JobTitle is not { Length: > MaxJobTitleLength }
                ? null
                : new InvariantFailure($"A job title is at most {MaxJobTitleLength} characters.").With("Max", MaxJobTitleLength);
    }
}

/// <summary>Whose seat a seat is: an employee's, or an external person's.</summary>
public enum HostSeatKind
{
    /// <summary>An employee of the tenant.</summary>
    Employee,

    /// <summary>Somebody from outside, on a piece of work.</summary>
    External,
}
