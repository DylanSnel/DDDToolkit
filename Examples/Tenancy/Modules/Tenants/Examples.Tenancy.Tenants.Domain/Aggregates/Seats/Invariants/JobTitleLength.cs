using DDDToolkit.Invariants;

namespace Examples.Tenancy.Tenants.Domain.Aggregates.Seats;

// The seat's own rule, next to the package's: nested in the seat, as a partial of it, because the generator finds
// a class's rules among its own nested types.
public sealed partial class Seat
{
    /// <summary>A job title is at most <see cref="MaxJobTitleLength"/> characters.</summary>
    public sealed class JobTitleLength : IInvariant<Seat>
    {
        /// <summary>The code this rule is reported by.</summary>
        public const string ViolationCode = "tenants.seat.job-title";

        /// <inheritdoc />
        public string Code => ViolationCode;

        /// <inheritdoc />
        public InvariantFailure? Check(Seat entity)
            => entity.JobTitle is not { Length: > MaxJobTitleLength }
                ? null
                : new InvariantFailure($"A job title is at most {MaxJobTitleLength} characters.").With("Max", MaxJobTitleLength);
    }
}
