using DDDToolkit.Invariants;
using DDDToolkit.Supporting.Tenancy.TestHost.Contracts;

namespace DDDToolkit.Supporting.Tenancy.TestHost.Domain;

/// <summary>The application's seat: the package's, with a job title and a rule about it.</summary>
[SeatAggregate<SeatId>]
public sealed partial class HostSeat
{
    /// <summary>The longest job title.</summary>
    public const int MaxJobTitleLength = 80;

    /// <summary>What the person does, or <see langword="null"/>.</summary>
    public string? JobTitle { get; private set; }

    /// <summary>Sets or clears the job title.</summary>
    public void ChangeJobTitle(string? jobTitle) => JobTitle = jobTitle;

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
